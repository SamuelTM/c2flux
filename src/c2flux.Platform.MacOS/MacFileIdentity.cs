using System.Runtime.InteropServices;

namespace c2flux
{
    // FileIdentityReader for macOS: device and file id from getattrlist(2),
    // following links. No change journal number.
    public static class MacFileIdentity
    {
        private const uint AttrCmnDevId = 0x00000002;
        private const uint AttrCmnFileId = 0x02000000;
        private const uint AttrCmnReturnedAttrs = 0x80000000;
        private const uint AttrCmnExtCloneId = 0x00000100;
        private const uint FsOptAttrCmnExtended = 0x00000020;

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

        // FileCloneIdReader for APFS. Other file systems do not return the id.
        // shortcut: a clone edited back to equal content gets its own id and
        // counts in full, use ATTR_CMNEXT_PRIVATESIZE if that ever matters.
        public static unsafe bool TryReadCloneId(string path, out ulong cloneId)
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
