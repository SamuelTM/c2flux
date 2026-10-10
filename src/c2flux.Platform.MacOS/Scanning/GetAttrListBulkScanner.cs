using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace c2flux
{
    // macOS scan chain: GetAttrListBulkScanner -> ManagedScanner.
    public static class MacScanners
    {
        public static ScannerPipeline CreatePipeline(AppSettings settings)
        {
            return new ScannerPipeline(Create(settings));
        }

        public static IReadOnlyList<IFileSystemScanner> Create(AppSettings settings)
        {
            return new IFileSystemScanner[]
            {
                new GetAttrListBulkScanner(settings),
                new ManagedScanner(settings),
            };
        }
    }

    // Reads each directory with getattrlistbulk(2): names, types, sizes and
    // dates of many entries per system call, instead of one lstat per file.
    // Walk, tree, sizes and progress are ManagedScanner's.
    //
    // Never leaves the root's volume (other volumes, /dev, network mounts stay
    // empty). When scanning /, /System/Volumes/Data is not entered: on APFS
    // the data volume is already reachable through the firmlinks at the root
    // (/Users, /Applications, ...) and would be counted twice.
    //
    // shortcut: hard links are counted once per link and only the logical
    // size (data fork length) is read; add ATTR_FILE_LINKCOUNT/ALLOCSIZE when
    // the UI gets an allocated-size mode.
    public sealed class GetAttrListBulkScanner : IFileSystemScanner
    {
        private const int BufferSize = 256 * 1024;
        private const string DataVolumePath = "/System/Volumes/Data";

        private readonly ManagedScanner _engine;

        public GetAttrListBulkScanner(AppSettings settings)
        {
            _engine = new ManagedScanner(settings, CreateReader);
        }

        public string Name => "GetAttrListBulkScanner";

        public string StatusTextKey => "Status.ScanRunning";

        public string FailureAlertKey => "Alert.NativeScanUnavailable";

        public ScannerSupport GetSupport(string rootPath)
        {
            if (!OperatingSystem.IsMacOS())
            {
                return ScannerSupport.NotSupported("Not macOS");
            }

            return _engine.GetSupport(rootPath);
        }

        public Task<FileSystemEntry> ScanAsync(
            string rootPath,
            IProgress<ScanProgress> progress,
            CancellationToken cancellationToken,
            PauseToken pauseToken)
        {
            return _engine.ScanAsync(rootPath, progress, cancellationToken, pauseToken);
        }

        private static DirectoryReader CreateReader(string rootPath)
        {
            int rootFd = OpenDirectory(rootPath);
            int rootDevice;

            try
            {
                rootDevice = GetDeviceId(rootFd, rootPath);
            }
            finally
            {
                close(rootFd);
            }

            bool skipDataVolume = rootPath == "/";

            return (directoryPath, entries) =>
            {
                if (skipDataVolume && directoryPath == DataVolumePath)
                {
                    return;
                }

                int fd = OpenDirectory(directoryPath);

                try
                {
                    if (GetDeviceId(fd, directoryPath) != rootDevice)
                    {
                        return;
                    }

                    ReadEntries(fd, directoryPath, entries);
                }
                finally
                {
                    close(fd);
                }
            };
        }

        private static unsafe void ReadEntries(int fd, string directoryPath, List<DirectoryEntryData> entries)
        {
            AttrList request = new AttrList
            {
                BitmapCount = AttrBitMapCount,
                CommonAttr = AttrCmnReturnedAttrs | AttrCmnName | AttrCmnError | AttrCmnObjType | AttrCmnModTime,
                FileAttr = AttrFileDataLength,
            };

            byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);

            try
            {
                fixed (byte* pointer = buffer)
                {
                    while (true)
                    {
                        int count = getattrlistbulk(fd, ref request, pointer, (nuint)buffer.Length, 0);

                        if (count < 0)
                        {
                            throw Error(Marshal.GetLastPInvokeError(), directoryPath);
                        }

                        if (count == 0)
                        {
                            return;
                        }

                        ReadOnlySpan<byte> data = new ReadOnlySpan<byte>(pointer, buffer.Length);
                        int offset = 0;

                        for (int index = 0; index < count; index++)
                        {
                            int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset));
                            ParseEntry(data.Slice(offset, length), entries);
                            offset += length;
                        }
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        // Layout (getattrlistbulk(2)): u32 length, attribute_set_t of the
        // attributes actually returned, then each returned attribute in the
        // order of its bit, except ATTR_CMN_ERROR, which comes first.
        private static void ParseEntry(ReadOnlySpan<byte> entry, List<DirectoryEntryData> entries)
        {
            int field = sizeof(uint);
            uint commonAttr = BinaryPrimitives.ReadUInt32LittleEndian(entry.Slice(field));
            uint fileAttr = BinaryPrimitives.ReadUInt32LittleEndian(entry.Slice(field + 12));
            field += AttributeSetSize;

            if ((commonAttr & AttrCmnError) != 0)
            {
                uint error = BinaryPrimitives.ReadUInt32LittleEndian(entry.Slice(field));
                field += sizeof(uint);

                if (error != 0)
                {
                    // shortcut: an entry the file system could not describe is
                    // left out; report it as skipped if users ever see gaps.
                    return;
                }
            }

            string name = null;

            if ((commonAttr & AttrCmnName) != 0)
            {
                int dataOffset = BinaryPrimitives.ReadInt32LittleEndian(entry.Slice(field));
                int nameLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry.Slice(field + 4));
                // attr_length counts the terminating NUL.
                name = Encoding.UTF8.GetString(entry.Slice(field + dataOffset, Math.Max(0, nameLength - 1)));
                field += 8;
            }

            uint objectType = 0;

            if ((commonAttr & AttrCmnObjType) != 0)
            {
                objectType = BinaryPrimitives.ReadUInt32LittleEndian(entry.Slice(field));
                field += sizeof(uint);
            }

            DateTime lastWriteTimeUtc = DateTime.MinValue;

            if ((commonAttr & AttrCmnModTime) != 0)
            {
                long seconds = BinaryPrimitives.ReadInt64LittleEndian(entry.Slice(field));
                long nanoseconds = BinaryPrimitives.ReadInt64LittleEndian(entry.Slice(field + 8));
                lastWriteTimeUtc = DateTime.UnixEpoch.AddTicks(seconds * TimeSpan.TicksPerSecond + nanoseconds / 100);
                field += 16;
            }

            long dataLength = 0;

            if ((fileAttr & AttrFileDataLength) != 0)
            {
                dataLength = BinaryPrimitives.ReadInt64LittleEndian(entry.Slice(field));
            }

            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            bool isDirectory = objectType == VDir;
            bool isLink = objectType == VLnk;

            entries.Add(new DirectoryEntryData(
                name,
                isDirectory,
                isLink,
                isDirectory || isLink || objectType != VReg ? 0 : dataLength,
                lastWriteTimeUtc));
        }

        private static unsafe int GetDeviceId(int fd, string path)
        {
            AttrList request = new AttrList
            {
                BitmapCount = AttrBitMapCount,
                CommonAttr = AttrCmnDevId,
            };

            byte* buffer = stackalloc byte[16];

            if (fgetattrlist(fd, ref request, buffer, 16, 0) != 0)
            {
                throw Error(Marshal.GetLastPInvokeError(), path);
            }

            // u32 length, then dev_t (int32).
            return *(int*)(buffer + 4);
        }

        private static int OpenDirectory(string path)
        {
            int fd = open(path, ORdOnly | ODirectory | OCloExec);

            if (fd < 0)
            {
                throw Error(Marshal.GetLastPInvokeError(), path);
            }

            return fd;
        }

        private static Exception Error(int errno, string path)
        {
            string message = path + ": " + Marshal.GetPInvokeErrorMessage(errno);

            return errno == EAcces || errno == EPerm
                ? new UnauthorizedAccessException(message)
                : new IOException(message, errno);
        }

        private const ushort AttrBitMapCount = 5;
        private const int AttributeSetSize = 5 * sizeof(uint);

        private const uint AttrCmnName = 0x00000001;
        private const uint AttrCmnDevId = 0x00000002;
        private const uint AttrCmnObjType = 0x00000008;
        private const uint AttrCmnModTime = 0x00000400;
        private const uint AttrCmnError = 0x20000000;
        private const uint AttrCmnReturnedAttrs = 0x80000000;
        private const uint AttrFileDataLength = 0x00000200;

        private const uint VReg = 1;
        private const uint VDir = 2;
        private const uint VLnk = 5;

        private const int ORdOnly = 0x0000;
        private const int ODirectory = 0x00100000;
        private const int OCloExec = 0x01000000;

        private const int EPerm = 1;
        private const int EAcces = 13;

        [StructLayout(LayoutKind.Sequential)]
        private struct AttrList
        {
            public ushort BitmapCount;
            public ushort Reserved;
            public uint CommonAttr;
            public uint VolAttr;
            public uint DirAttr;
            public uint FileAttr;
            public uint ForkAttr;
        }

        [DllImport("libc", SetLastError = true)]
        private static extern int open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

        [DllImport("libc", SetLastError = true)]
        private static extern int close(int fd);

        [DllImport("libc", SetLastError = true)]
        private static extern unsafe int getattrlistbulk(int dirfd, ref AttrList attrList, byte* attrBuf, nuint attrBufSize, ulong options);

        [DllImport("libc", SetLastError = true)]
        private static extern unsafe int fgetattrlist(int fd, ref AttrList attrList, byte* attrBuf, nuint attrBufSize, uint options);
    }
}
