using System.Runtime.InteropServices;

namespace c2flux
{
    // FileIdentityReader for Linux: device and inode from statx(2), following
    // links. struct statx has the same layout on every architecture.
    public static class LinuxFileIdentity
    {
        private const int AtFdCwd = -100;
        private const uint StatxIno = 0x00000100;
        private const int StatxSize = 256;
        private const int InodeOffset = 32;
        private const int DevMajorOffset = 136;
        private const int DevMinorOffset = 140;

        public static unsafe bool TryRead(string path, out FileIdentity identity, out long usn, out bool hasUsn)
        {
            usn = 0;
            hasUsn = false;
            identity = default;

            byte* buffer = stackalloc byte[StatxSize];

            if (statx(AtFdCwd, path, 0, StatxIno, buffer) != 0)
            {
                return false;
            }

            ulong device = ((ulong)*(uint*)(buffer + DevMajorOffset) << 32) | *(uint*)(buffer + DevMinorOffset);
            ulong inode = *(ulong*)(buffer + InodeOffset);
            identity = new FileIdentity(device, inode, 0);
            return true;
        }

        [DllImport("libc", SetLastError = true)]
        private static extern unsafe int statx(int dirfd, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, uint mask, byte* buffer);
    }
}
