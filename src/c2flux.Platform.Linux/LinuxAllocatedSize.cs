using System.Runtime.InteropServices;

namespace c2flux
{
    // What a file occupies on disk (st_blocks * 512, as du shows), from
    // statx(2) without following links. Smaller than the length for a sparse
    // file (a VM or Docker disk image: 228 GB long, 3 GB on disk).
    public static class LinuxAllocatedSize
    {
        private const int AtFdCwd = -100;
        private const int AtSymlinkNoFollow = 0x100;
        private const uint StatxBlocks = 0x00000400;
        private const int StatxSize = 256;
        private const int BlocksOffset = 48;

        public static unsafe bool TryRead(string path, out long bytes)
        {
            bytes = 0;
            byte* buffer = stackalloc byte[StatxSize];

            if (statx(AtFdCwd, path, AtSymlinkNoFollow, StatxBlocks, buffer) != 0 || (*(uint*)buffer & StatxBlocks) == 0)
            {
                return false;
            }

            bytes = (long)(*(ulong*)(buffer + BlocksOffset) * 512);
            return true;
        }

        [DllImport("libc", SetLastError = true)]
        private static extern unsafe int statx(int dirfd, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, uint mask, byte* buffer);
    }
}
