using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace c2flux
{
    // FileExtentReader for Linux, from the FIEMAP ioctl: covers file systems
    // with reflinks (Btrfs, XFS...). File systems without FIEMAP (NFS, SMB)
    // or without shared extents (ext4) count in full, which is right for ext4.
    // Btrfs subvolumes have their own device numbers but one block space, so
    // Btrfs extents are named after the file system's UUID.
    public static class LinuxFileExtents
    {
        private const ulong FsIocFiemap = 0xC020660B;
        private const int HeaderSize = 32;
        private const int ExtentSize = 56;
        private const int ExtentsPerCall = 512;
        private const int MappedExtentsOffset = 20;
        private const int ExtentCountOffset = 24;
        private const int PhysicalOffset = 8;
        private const int LengthOffset = 16;
        private const int FlagsOffset = 40;

        private const uint ExtentLast = 0x00000001;
        private const uint ExtentUnknown = 0x00000002;
        private const uint ExtentNotAligned = 0x00000100;
        private const uint ExtentShared = 0x00002000;

        private const ulong BtrfsIocFsInfo = 0x8400941F;
        private const int BtrfsFsInfoSize = 1024;
        private const int BtrfsFsIdOffset = 16;
        private const long ZfsMagic = 0x2FC12FC1;
        private const int StatfsSize = 256;

        public static unsafe bool TryRead(string path, out IReadOnlyList<FileExtent> extents, out ulong volume)
        {
            extents = Array.Empty<FileExtent>();
            volume = 0;
            List<FileExtent> found = new List<FileExtent>();
            bool shared = false;
            byte[] buffer = new byte[HeaderSize + ExtentsPerCall * ExtentSize];

            try
            {
                using SafeFileHandle handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                int descriptor = (int)handle.DangerousGetHandle();
                ulong start = 0;
                bool last = false;

                while (!last)
                {
                    Array.Clear(buffer);

                    fixed (byte* pointer = buffer)
                    {
                        *(ulong*)pointer = start;
                        *(ulong*)(pointer + 8) = ulong.MaxValue;
                        *(uint*)(pointer + ExtentCountOffset) = ExtentsPerCall;

                        if (ioctl(descriptor, FsIocFiemap, pointer) != 0)
                        {
                            return false;
                        }

                        uint mapped = *(uint*)(pointer + MappedExtentsOffset);

                        if (mapped == 0)
                        {
                            break;
                        }

                        for (uint i = 0; i < mapped; i++)
                        {
                            byte* extent = pointer + HeaderSize + i * ExtentSize;
                            uint flags = *(uint*)(extent + FlagsOffset);

                            // Not yet on disk, or inside a metadata block: no usable place.
                            if ((flags & (ExtentUnknown | ExtentNotAligned)) != 0)
                            {
                                return false;
                            }

                            ulong length = *(ulong*)(extent + LengthOffset);
                            found.Add(new FileExtent(*(ulong*)(extent + PhysicalOffset), length));
                            shared |= (flags & ExtentShared) != 0;
                            start = *(ulong*)extent + length;

                            last |= (flags & ExtentLast) != 0;
                        }
                    }
                }

                extents = found;
                volume = ReadBtrfsId(descriptor);
                return shared;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return false;
            }
        }

        // For FileIdentities.HiddenCloneChecker: ZFS clones blocks but shows
        // no extents, and no per-file count of what it shares.
        public static unsafe bool MayHideClones(string path)
        {
            byte* buffer = stackalloc byte[StatfsSize];
            return statfs(path, buffer) == 0 && *(long*)buffer == ZfsMagic;
        }

        // 0 on other file systems, which reject the ioctl.
        private static unsafe ulong ReadBtrfsId(int descriptor)
        {
            byte* info = stackalloc byte[BtrfsFsInfoSize];

            return ioctl(descriptor, BtrfsIocFsInfo, info) == 0
                ? *(ulong*)(info + BtrfsFsIdOffset) ^ *(ulong*)(info + BtrfsFsIdOffset + 8)
                : 0;
        }

        [DllImport("libc", SetLastError = true)]
        private static extern unsafe int ioctl(int fd, ulong request, byte* argument);

        [DllImport("libc", SetLastError = true)]
        private static extern unsafe int statfs([MarshalAs(UnmanagedType.LPUTF8Str)] string path, byte* buffer);
    }
}
