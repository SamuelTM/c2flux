using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace c2flux.Screenshots
{
    internal static class WindowCapture
    {
        private const uint PrintWindowRenderFullContent = 0x00000002;

        // Captures the whole window, including the title bar, through DWM
        // (PrintWindow with PW_RENDERFULLCONTENT). This works even when the
        // window is partly off screen or covered. Falls back to copying the
        // screen area when the result is a blank image, which happens with
        // some drivers.
        // A screen area as the user sees it, for things that only make sense in
        // context (a menu open over the main window).
        public static Bitmap CaptureScreen(Rectangle area)
        {
            Bitmap bitmap = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppArgb);

            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(area.Location, Point.Empty, area.Size);
            }

            return bitmap;
        }

        // Window bounds without the invisible resize borders that Windows 10/11
        // add around top-level windows (Control.Bounds includes them).
        public static Rectangle GetVisibleBounds(Control window)
        {
            if (DwmGetWindowAttribute(window.Handle, DwmwaExtendedFrameBounds, out Rect rect, Marshal.SizeOf<Rect>()) == 0)
            {
                return Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            }

            return window.Bounds;
        }

        public static Bitmap Capture(Control form, out string method)
        {
            form.Refresh();

            if (!GetWindowRect(form.Handle, out Rect rect))
            {
                throw new InvalidOperationException("GetWindowRect failed.");
            }

            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;

            if (width <= 0 || height <= 0)
            {
                throw new InvalidOperationException(string.Format("Window has no area ({0}x{1}).", width, height));
            }

            Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);

            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                IntPtr hdc = graphics.GetHdc();
                bool printed;

                try
                {
                    printed = PrintWindow(form.Handle, hdc, PrintWindowRenderFullContent);
                }
                finally
                {
                    graphics.ReleaseHdc(hdc);
                }

                if (printed && !IsBlank(bitmap))
                {
                    method = "PrintWindow";
                    return bitmap;
                }

                graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, new Size(width, height));
            }

            method = "CopyFromScreen";
            return bitmap;
        }

        private static bool IsBlank(Bitmap bitmap)
        {
            Color first = bitmap.GetPixel(0, 0);
            int stepX = Math.Max(1, bitmap.Width / 16);
            int stepY = Math.Max(1, bitmap.Height / 16);

            for (int y = 0; y < bitmap.Height; y += stepY)
            {
                for (int x = 0; x < bitmap.Width; x += stepX)
                {
                    if (bitmap.GetPixel(x, y) != first)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

        private const int DwmwaExtendedFrameBounds = 9;

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out Rect value, int size);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);
    }

    internal static class Display
    {
        private const int EnumCurrentSettings = -1;
        private const int DmPelsWidth = 0x00080000;
        private const int DmPelsHeight = 0x00100000;
        private const int DispChangeSuccessful = 0;

        // Hosted CI runners start with a small desktop (often 1024x768), which
        // would clamp the main window. Returns a description of the outcome.
        public static string TrySetResolution(int width, int height)
        {
            DevMode mode = new DevMode { dmSize = (short)Marshal.SizeOf<DevMode>() };

            if (!EnumDisplaySettings(null, EnumCurrentSettings, ref mode))
            {
                return "unknown (EnumDisplaySettings failed)";
            }

            string before = mode.dmPelsWidth + "x" + mode.dmPelsHeight;

            if (mode.dmPelsWidth == width && mode.dmPelsHeight == height)
            {
                return before;
            }

            mode.dmPelsWidth = width;
            mode.dmPelsHeight = height;
            mode.dmFields = DmPelsWidth | DmPelsHeight;

            int result = ChangeDisplaySettings(ref mode, 0);

            DevMode after = new DevMode { dmSize = (short)Marshal.SizeOf<DevMode>() };
            EnumDisplaySettings(null, EnumCurrentSettings, ref after);
            string now = after.dmPelsWidth + "x" + after.dmPelsHeight;

            return result == DispChangeSuccessful
                ? now + " (changed from " + before + ")"
                : now + " (could not change from " + before + ", ChangeDisplaySettings returned " + result + ")";
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DevMode
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmDeviceName;
            public short dmSpecVersion;
            public short dmDriverVersion;
            public short dmSize;
            public short dmDriverExtra;
            public int dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public int dmDisplayOrientation;
            public int dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;
            public int dmDisplayFlags;
            public int dmDisplayFrequency;
            public int dmICMMethod;
            public int dmICMIntent;
            public int dmMediaType;
            public int dmDitherType;
            public int dmReserved1;
            public int dmReserved2;
            public int dmPanningWidth;
            public int dmPanningHeight;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DevMode devMode);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int ChangeDisplaySettings(ref DevMode devMode, int flags);
    }
}
