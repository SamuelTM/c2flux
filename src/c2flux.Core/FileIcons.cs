using System;

namespace c2flux
{
    // An icon as premultiplied 32-bit BGRA pixels, top row first.
    public sealed class IconPixels
    {
        public IconPixels(int width, int height, byte[] bgra)
        {
            Width = width;
            Height = height;
            Bgra = bgra;
        }

        public int Width { get; }
        public int Height { get; }
        public byte[] Bgra { get; }
    }

    public enum FileIconKind
    {
        // The file at the path, or its type when it does not exist.
        File,
        Folder,
        // The volume whose root is the path.
        Volume,
        // The "open folder" icon of the toolbar.
        OpenFolder,
    }

    // Where the UI gets the system's icons. Each platform registers a reader
    // at startup (Windows: the shell, macOS: NSWorkspace); without one the UI
    // draws generic icons.
    public static class FileIcons
    {
        // (path, kind, size in pixels) -> icon, or null when there is none.
        public static Func<string, FileIconKind, int, IconPixels> Reader { get; set; }

        public static IconPixels Read(string path, FileIconKind kind, int size)
        {
            try
            {
                return Reader?.Invoke(path, kind, size);
            }
            catch (Exception exception) when (exception is DllNotFoundException || exception is EntryPointNotFoundException || exception is System.Runtime.InteropServices.ExternalException)
            {
                return null;
            }
        }
    }
}
