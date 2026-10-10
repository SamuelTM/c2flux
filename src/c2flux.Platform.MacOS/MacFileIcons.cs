using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace c2flux
{
    // FileIcons.Reader for macOS: Finder's icons from NSWorkspace, drawn into
    // a premultiplied BGRA bitmap context. Files are looked up by type (no
    // disk access); folders and volumes by their path.
    [SupportedOSPlatform("macos")]
    public static class MacFileIcons
    {
        private const string ObjC = "/usr/lib/libobjc.dylib";
        private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
        private const uint PremultipliedFirstLittleEndian = 2 | (2 << 12);

        // NSWorkspace lives in AppKit, which only a GUI process has loaded
        // (objc_getClass returns nil otherwise).
        private static readonly bool AppKitLoaded =
            NativeLibrary.TryLoad("/System/Library/Frameworks/AppKit.framework/AppKit", out _);

        public static IconPixels Read(string path, FileIconKind kind, int size)
        {
            if (!AppKitLoaded)
            {
                return null;
            }

            IntPtr pool = objc_autoreleasePoolPush();

            try
            {
                IntPtr image = GetImage(path, kind);
                return image == IntPtr.Zero ? null : Rasterize(image, size);
            }
            finally
            {
                objc_autoreleasePoolPop(pool);
            }
        }

        private static IntPtr GetImage(string path, FileIconKind kind)
        {
            IntPtr workspace = Send(Class("NSWorkspace"), "sharedWorkspace");

            switch (kind)
            {
                case FileIconKind.File:
                    string extension = Path.GetExtension(path ?? string.Empty).TrimStart('.');
                    return SendString(workspace, "iconForFileType:", extension.Length == 0 ? "public.data" : extension);

                case FileIconKind.Folder:
                case FileIconKind.OpenFolder:
                    // "fldr": the generic folder type.
                    return SendString(workspace, "iconForFileType:", "'fldr'");

                default:
                    return SendString(workspace, "iconForFile:", path ?? "/");
            }
        }

        private static unsafe IconPixels Rasterize(IntPtr image, int size)
        {
            IntPtr cgImage = objc_msgSend_CGImage(image, Selector("CGImageForProposedRect:context:hints:"), IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

            if (cgImage == IntPtr.Zero)
            {
                return null;
            }

            byte[] pixels = new byte[size * size * 4];
            IntPtr colorSpace = CGColorSpaceCreateDeviceRGB();

            fixed (byte* buffer = pixels)
            {
                IntPtr context = CGBitmapContextCreate((IntPtr)buffer, (UIntPtr)size, (UIntPtr)size, (UIntPtr)8, (UIntPtr)(size * 4), colorSpace, PremultipliedFirstLittleEndian);

                if (context == IntPtr.Zero)
                {
                    CGColorSpaceRelease(colorSpace);
                    return null;
                }

                CGContextSetInterpolationQuality(context, 3);
                CGContextDrawImage(context, new CGRect { Width = size, Height = size }, cgImage);
                CGContextRelease(context);
            }

            CGColorSpaceRelease(colorSpace);
            return new IconPixels(size, size, pixels);
        }

        private static IntPtr Class(string name) => objc_getClass(name);

        private static IntPtr Selector(string name) => sel_registerName(name);

        private static IntPtr Send(IntPtr target, string selector) => objc_msgSend(target, Selector(selector));

        private static IntPtr SendString(IntPtr target, string selector, string argument)
        {
            IntPtr text = objc_msgSend_string(Class("NSString"), Selector("stringWithUTF8String:"), argument);
            return objc_msgSend_id(target, Selector(selector), text);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CGRect
        {
            public double X;
            public double Y;
            public double Width;
            public double Height;
        }

        [DllImport(ObjC)]
        private static extern IntPtr objc_getClass(string name);

        [DllImport(ObjC)]
        private static extern IntPtr sel_registerName(string name);

        [DllImport(ObjC)]
        private static extern IntPtr objc_autoreleasePoolPush();

        [DllImport(ObjC)]
        private static extern void objc_autoreleasePoolPop(IntPtr pool);

        [DllImport(ObjC, EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend(IntPtr target, IntPtr selector);

        [DllImport(ObjC, EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend_id(IntPtr target, IntPtr selector, IntPtr argument);

        [DllImport(ObjC, EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend_string(IntPtr target, IntPtr selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string argument);

        [DllImport(ObjC, EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend_CGImage(IntPtr target, IntPtr selector, IntPtr rect, IntPtr context, IntPtr hints);

        [DllImport(CoreGraphics)]
        private static extern IntPtr CGColorSpaceCreateDeviceRGB();

        [DllImport(CoreGraphics)]
        private static extern void CGColorSpaceRelease(IntPtr space);

        [DllImport(CoreGraphics)]
        private static extern IntPtr CGBitmapContextCreate(IntPtr data, UIntPtr width, UIntPtr height, UIntPtr bitsPerComponent, UIntPtr bytesPerRow, IntPtr space, uint bitmapInfo);

        [DllImport(CoreGraphics)]
        private static extern void CGContextSetInterpolationQuality(IntPtr context, int quality);

        [DllImport(CoreGraphics)]
        private static extern void CGContextDrawImage(IntPtr context, CGRect rect, IntPtr image);

        [DllImport(CoreGraphics)]
        private static extern void CGContextRelease(IntPtr context);
    }
}
