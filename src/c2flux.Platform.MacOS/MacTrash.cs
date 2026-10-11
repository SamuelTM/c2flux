using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace c2flux
{
    // FileManager.TrashMover for macOS: NSFileManager's trashItemAtURL, what
    // Finder's "Move to Trash" does (the item can be put back from there).
    [SupportedOSPlatform("macos")]
    public static class MacTrash
    {
        private const string ObjC = "/usr/lib/libobjc.dylib";

        private static readonly bool FoundationLoaded =
            NativeLibrary.TryLoad("/System/Library/Frameworks/Foundation.framework/Foundation", out _);

        public static void Move(string path)
        {
            if (!FoundationLoaded)
            {
                throw new IOException("Foundation could not be loaded.");
            }

            IntPtr pool = objc_autoreleasePoolPush();

            try
            {
                IntPtr text = objc_msgSend_string(objc_getClass("NSString"), sel_registerName("stringWithUTF8String:"), Path.GetFullPath(path));
                IntPtr url = objc_msgSend_id(objc_getClass("NSURL"), sel_registerName("fileURLWithPath:"), text);
                IntPtr manager = objc_msgSend(objc_getClass("NSFileManager"), sel_registerName("defaultManager"));

                if (objc_msgSend_trash(manager, sel_registerName("trashItemAtURL:resultingItemURL:error:"), url, IntPtr.Zero, out IntPtr error) == 0)
                {
                    throw new IOException(Describe(error));
                }
            }
            finally
            {
                objc_autoreleasePoolPop(pool);
            }
        }

        // NSError's localizedDescription, as Finder words it.
        private static string Describe(IntPtr error)
        {
            if (error == IntPtr.Zero)
            {
                return "The item could not be moved to the Trash.";
            }

            IntPtr description = objc_msgSend(error, sel_registerName("localizedDescription"));
            IntPtr utf8 = description == IntPtr.Zero ? IntPtr.Zero : objc_msgSend(description, sel_registerName("UTF8String"));
            return utf8 == IntPtr.Zero ? "The item could not be moved to the Trash." : Marshal.PtrToStringUTF8(utf8);
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

        // BOOL is a signed char.
        [DllImport(ObjC, EntryPoint = "objc_msgSend")]
        private static extern sbyte objc_msgSend_trash(IntPtr target, IntPtr selector, IntPtr url, IntPtr resultingUrl, out IntPtr error);
    }
}
