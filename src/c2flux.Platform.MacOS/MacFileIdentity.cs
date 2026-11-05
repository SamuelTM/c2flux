using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace c2flux
{
    // FileIdentityReader for macOS: device, file id and change stamp from
    // getattrlist(2), following links.
    public static class MacFileIdentity
    {
        private const uint AttrCmnDevId = 0x00000002;
        private const uint AttrCmnModTime = 0x00000400;
        private const uint AttrCmnChgTime = 0x00000800;
        private const uint AttrCmnFileId = 0x02000000;
        private const uint AttrCmnReturnedAttrs = 0x80000000;
        private const uint AttrCmnExtCloneId = 0x00000100;
        private const uint FsOptAttrCmnExtended = 0x00000020;
        private const int FLog2PhysExt = 65;
        private const ulong CloneIdExtents = 1UL << 63;
        private const int StatfsSize = 2168;
        private const int FileSystemTypeOffset = 72;
        private const int FileSystemTypeSize = 16;

        public static unsafe bool TryRead(string path, out FileIdentity identity, out long usn, out bool hasUsn)
        {
            usn = 0;
            hasUsn = false;
            identity = default;

            AttrList request = new AttrList { BitmapCount = 5, CommonAttr = AttrCmnDevId | AttrCmnModTime | AttrCmnChgTime | AttrCmnFileId };
            byte* buffer = stackalloc byte[48];

            if (getattrlist(path, ref request, buffer, 48, 0) != 0)
            {
                return false;
            }

            // u32 length, dev_t (int32), two timespecs (i64 seconds, i64
            // nanoseconds), u64 file id. Attributes are 4-byte aligned.
            uint device = *(uint*)(buffer + 4);
            ulong fileId = *(ulong*)(buffer + 40);
            identity = new FileIdentity(device, fileId, 0);
            hasUsn = FileIdentities.ChangeStamp(*(long*)(buffer + 8), (uint)*(long*)(buffer + 16), *(long*)(buffer + 24), (uint)*(long*)(buffer + 32), out usn) &&
                !IsNetworkShare(path);
            return true;
        }

        // The attributes of a share can be out of date for a while, so they
        // cannot tell that a file is unchanged.
        private static bool IsNetworkShare(string path) => ReadFileSystemType(path) is "nfs" or "smbfs";

        // FileExtentReader for APFS: the file's blocks. Compressed files have
        // no block map, so their clone id is one extent, shared whole or not
        // at all, above any byte offset. Other file systems return neither.
        public static bool TryReadExtents(string path, out IReadOnlyList<FileExtent> extents, out ulong volume)
        {
            volume = 0;

            if (TryReadBlocks(path, out List<FileExtent> blocks))
            {
                extents = blocks;
                return blocks.Count > 0;
            }

            bool known = TryReadCloneId(path, out ulong cloneId);
            extents = known ? new[] { new FileExtent(CloneIdExtents | cloneId, 1) } : Array.Empty<FileExtent>();
            return known;
        }

        // For FileIdentities.HiddenCloneChecker: ZFS and the disks behind a
        // network share clone blocks without showing which. shortcut: a ZFS
        // dataset mimicking hfs or apfs reports that name and gets no mark,
        // until the port offers a way to tell them apart.
        public static bool MayHideClones(string path) => ReadFileSystemType(path) is "zfs" or "nfs" or "smbfs";

        // f_fstypename from statfs(2), null when it cannot be read. Intel Macs
        // need the $INODE64 entry point for this struct layout.
        public static unsafe string ReadFileSystemType(string path)
        {
            byte* buffer = stackalloc byte[StatfsSize];
            int result = RuntimeInformation.ProcessArchitecture == Architecture.X64
                ? statfsInode64(path, buffer)
                : statfs(path, buffer);

            return result == 0
                ? Marshal.PtrToStringUTF8((IntPtr)(buffer + FileSystemTypeOffset), new ReadOnlySpan<byte>(buffer + FileSystemTypeOffset, FileSystemTypeSize).IndexOf((byte)0))
                : null;
        }

        private static bool TryReadBlocks(string path, out List<FileExtent> blocks)
        {
            blocks = new List<FileExtent>();

            try
            {
                using SafeFileHandle handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                int descriptor = (int)handle.DangerousGetHandle();
                long length = RandomAccess.GetLength(handle);
                long offset = 0;

                while (offset < length)
                {
                    Log2Phys query = new Log2Phys { ContigBytes = length - offset, DevOffset = offset };

                    // Apple's arm64 ABI passes variadic arguments on the stack:
                    // filler parameters use up the registers.
                    int result = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                        ? fcntl(descriptor, FLog2PhysExt, 0, 0, 0, 0, 0, 0, ref query)
                        : fcntl(descriptor, FLog2PhysExt, ref query);

                    if (result != 0 || query.ContigBytes <= 0)
                    {
                        return false;
                    }

                    // -1 is a hole.
                    if (query.DevOffset >= 0)
                    {
                        blocks.Add(new FileExtent((ulong)query.DevOffset, (ulong)query.ContigBytes));
                    }

                    offset += query.ContigBytes;
                }

                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static unsafe bool TryReadCloneId(string path, out ulong cloneId)
        {
            cloneId = 0;

            AttrList request = new AttrList { BitmapCount = 5, CommonAttr = AttrCmnReturnedAttrs, ForkAttr = AttrCmnExtCloneId };
            byte* buffer = stackalloc byte[32];

            if (getattrlist(path, ref request, buffer, 32, FsOptAttrCmnExtended) != 0)
            {
                return false;
            }

            // u32 length, attribute_set_t (common, vol, dir, file, fork), u64 clone id.
            if ((*(uint*)(buffer + 20) & AttrCmnExtCloneId) == 0)
            {
                return false;
            }

            cloneId = *(ulong*)(buffer + 24);
            return cloneId != 0;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct Log2Phys
        {
            public uint Flags;
            public long ContigBytes;
            public long DevOffset;
        }

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
        private static extern unsafe int statfs([MarshalAs(UnmanagedType.LPUTF8Str)] string path, byte* buffer);

        [DllImport("libc", EntryPoint = "statfs$INODE64", SetLastError = true)]
        private static extern unsafe int statfsInode64([MarshalAs(UnmanagedType.LPUTF8Str)] string path, byte* buffer);

        [DllImport("libc", SetLastError = true)]
        private static extern int fcntl(int descriptor, int command, ref Log2Phys query);

        [DllImport("libc", SetLastError = true)]
        private static extern int fcntl(int descriptor, int command, long filler2, long filler3, long filler4, long filler5, long filler6, long filler7, ref Log2Phys query);

        [DllImport("libc", SetLastError = true)]
        private static extern unsafe int getattrlist([MarshalAs(UnmanagedType.LPUTF8Str)] string path, ref AttrList attrList, byte* attrBuf, nuint attrBufSize, uint options);
    }
}
