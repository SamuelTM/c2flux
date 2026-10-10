using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace c2flux
{
    // FileIcons.Reader for Windows: the shell's small icons (SHGetFileInfo,
    // SHGetStockIconInfo), as the WinForms ShellIconService took them,
    // drawn into a 32-bit DIB to read their pixels.
    [SupportedOSPlatform("windows")]
    public static class WindowsFileIcons
    {
        private const uint SHGFI_ICON = 0x100;
        private const uint SHGFI_SMALLICON = 0x1;
        private const uint SHGFI_LARGEICON = 0x0;
        private const uint SHGFI_USEFILEATTRIBUTES = 0x10;
        private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
        private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
        private const uint SIID_FOLDEROPEN = 4;
        private const uint SHGSI_ICON = 0x100;
        private const uint SHGSI_SMALLICON = 0x1;

        public static IconPixels Read(string path, FileIconKind kind, int size)
        {
            IntPtr icon = kind == FileIconKind.OpenFolder ? GetStockIcon(size) : GetFileIcon(path, kind, size);

            if (icon == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return ToPixels(icon, size);
            }
            finally
            {
                DestroyIcon(icon);
            }
        }

        private static IntPtr GetFileIcon(string path, FileIconKind kind, int size)
        {
            SHFILEINFO info = new SHFILEINFO();
            uint flags = SHGFI_ICON | (size <= 16 ? SHGFI_SMALLICON : SHGFI_LARGEICON);
            uint attributes = 0;

            // Files and folders by type (fast, no disk access); volumes by
            // their real root so each drive gets its own icon.
            if (kind != FileIconKind.Volume)
            {
                flags |= SHGFI_USEFILEATTRIBUTES;
                attributes = kind == FileIconKind.Folder ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;
            }

            IntPtr result = SHGetFileInfo(path ?? string.Empty, attributes, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), flags);
            return result == IntPtr.Zero ? IntPtr.Zero : info.hIcon;
        }

        private static IntPtr GetStockIcon(int size)
        {
            SHSTOCKICONINFO info = new SHSTOCKICONINFO { cbSize = (uint)Marshal.SizeOf<SHSTOCKICONINFO>() };
            return SHGetStockIconInfo(SIID_FOLDEROPEN, SHGSI_ICON | (size <= 16 ? SHGSI_SMALLICON : 0), ref info) == 0 ? info.hIcon : IntPtr.Zero;
        }

        // The icon's color bitmap read with GetDIBits (drawing it with GDI
        // would lose the alpha channel). Icons without alpha take it from
        // their mask. Returned premultiplied.
        private static IconPixels ToPixels(IntPtr icon, int size)
        {
            if (!GetIconInfo(icon, out ICONINFO info))
            {
                return null;
            }

            IntPtr dc = CreateCompatibleDC(IntPtr.Zero);

            try
            {
                if (info.hbmColor == IntPtr.Zero || GetObject(info.hbmColor, Marshal.SizeOf<BITMAP>(), out BITMAP bitmap) == 0)
                {
                    return null;
                }

                int width = bitmap.bmWidth;
                int height = bitmap.bmHeight;
                byte[] color = ReadBits(dc, info.hbmColor, width, height);
                bool hasAlpha = false;

                for (int index = 3; index < color.Length; index += 4)
                {
                    hasAlpha |= color[index] != 0;
                }

                byte[] mask = hasAlpha ? null : ReadBits(dc, info.hbmMask, width, height);

                for (int index = 0; index < color.Length; index += 4)
                {
                    byte alpha = hasAlpha ? color[index + 3] : mask[index] == 0 ? (byte)255 : (byte)0;
                    color[index] = (byte)(color[index] * alpha / 255);
                    color[index + 1] = (byte)(color[index + 1] * alpha / 255);
                    color[index + 2] = (byte)(color[index + 2] * alpha / 255);
                    color[index + 3] = alpha;
                }

                return new IconPixels(width, height, color);
            }
            finally
            {
                DeleteDC(dc);

                if (info.hbmColor != IntPtr.Zero)
                {
                    DeleteObject(info.hbmColor);
                }

                if (info.hbmMask != IntPtr.Zero)
                {
                    DeleteObject(info.hbmMask);
                }
            }
        }

        private static byte[] ReadBits(IntPtr dc, IntPtr bitmap, int width, int height)
        {
            BITMAPINFOHEADER header = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = -height,
                biPlanes = 1,
                biBitCount = 32,
            };
            byte[] pixels = new byte[width * height * 4];
            GetDIBits(dc, bitmap, 0, (uint)height, pixels, ref header, 0);
            return pixels;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ICONINFO
        {
            public bool fIcon;
            public int xHotspot;
            public int yHotspot;
            public IntPtr hbmMask;
            public IntPtr hbmColor;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAP
        {
            public int bmType;
            public int bmWidth;
            public int bmHeight;
            public int bmWidthBytes;
            public ushort bmPlanes;
            public ushort bmBitsPixel;
            public IntPtr bmBits;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHSTOCKICONINFO
        {
            public uint cbSize;
            public IntPtr hIcon;
            public int iSysImageIndex;
            public int iIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szPath;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth;
            public int biHeight;
            public ushort biPlanes;
            public ushort biBitCount;
            public uint biCompression;
            public uint biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public uint biClrUsed;
            public uint biClrImportant;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [DllImport("shell32.dll")]
        private static extern int SHGetStockIconInfo(uint siid, uint uFlags, ref SHSTOCKICONINFO psii);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll")]
        private static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);

        [DllImport("gdi32.dll")]
        private static extern int GetObject(IntPtr h, int c, out BITMAP pv);

        [DllImport("gdi32.dll")]
        private static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint cLines, byte[] lpvBits, ref BITMAPINFOHEADER lpbmi, uint usage);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr ho);
    }
}
