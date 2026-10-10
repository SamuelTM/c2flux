using System.Runtime.InteropServices;

namespace c2flux
{
    // FileIdentityReader for macOS: device and file id from getattrlist(2),
    // following links. No change journal number.
    public static class MacFileIdentity
    {
        private const uint AttrCmnDevId = 0x00000002;
        private const uint AttrCmnFileId = 0x02000000;

        public static unsafe bool TryRead(string path, out FileIdentity identity, out long usn, out bool hasUsn)
        {
            usn = 0;
            hasUsn = false;
            identity = default;

            AttrList request = new AttrList { BitmapCount = 5, CommonAttr = AttrCmnDevId | AttrCmnFileId };
            byte* buffer = stackalloc byte[32];

            if (getattrlist(path, ref request, buffer, 32, 0) != 0)
            {
                return false;
            }

            // u32 length, dev_t (int32), u64 file id (attributes are 4-byte aligned).
            uint device = *(uint*)(buffer + 4);
            ulong fileId = *(ulong*)(buffer + 8);
            identity = new FileIdentity(device, fileId, 0);
            return true;
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
        private static extern unsafe int getattrlist([MarshalAs(UnmanagedType.LPUTF8Str)] string path, ref AttrList attrList, byte* attrBuf, nuint attrBufSize, uint options);
    }
}
