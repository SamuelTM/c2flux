using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace c2flux
{
    // FileManager.TrashMover for Windows: the shell's delete with undo, which
    // is the Recycle Bin, without its dialogs.
    [SupportedOSPlatform("windows")]
    public static class WindowsTrash
    {
        private const uint FO_DELETE = 3;
        private const ushort FOF_SILENT = 0x0004;
        private const ushort FOF_NOCONFIRMATION = 0x0010;
        private const ushort FOF_ALLOWUNDO = 0x0040;
        private const ushort FOF_NOERRORUI = 0x0400;

        public static void Move(string path)
        {
            SHFILEOPSTRUCTW operation = new SHFILEOPSTRUCTW
            {
                wFunc = FO_DELETE,
                // A list of paths, each ended by \0, the list by another \0.
                pFrom = Path.GetFullPath(path) + "\0\0",
                fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT,
            };
            int result = SHFileOperationW(ref operation);

            if (result != 0 || operation.fAnyOperationsAborted)
            {
                throw new IOException(string.Format("The shell could not move the item to the Recycle Bin (code 0x{0:X}).", result));
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEOPSTRUCTW
        {
            public IntPtr hwnd;
            public uint wFunc;
            public string pFrom;
            public string pTo;
            public ushort fFlags;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            public string lpszProgressTitle;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHFileOperationW(ref SHFILEOPSTRUCTW lpFileOp);
    }
}
