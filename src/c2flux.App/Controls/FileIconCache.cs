using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace c2flux
{
    // The system icons of files, folders and volumes as Avalonia images, read
    // once per kind (files: per extension). Without a platform reader
    // (Linux) the icons are drawn.
    public static class FileIconCache
    {
        private const int Size = 16;
        private static readonly Dictionary<string, IImage> Cache = new Dictionary<string, IImage>(StringComparer.OrdinalIgnoreCase);

        public static IImage Folder => Get("folder", null, FileIconKind.Folder);

        public static IImage OpenFolder => Get("open-folder", null, FileIconKind.OpenFolder);

        // The icon of a volume root; each volume has its own on Windows and macOS.
        public static IImage Volume(string rootPath) => Get("volume:" + rootPath, rootPath, FileIconKind.Volume);

        // The icon of a file's type, by extension.
        public static IImage FileType(string fileName)
        {
            string extension = Path.GetExtension(fileName ?? string.Empty);
            return Get("file:" + extension, "file" + extension, FileIconKind.File);
        }

        // Tree and partition icons: the volume icon for a scanned volume root,
        // otherwise folder or file.
        public static IImage ForEntry(FileSystemEntry entry, bool isVolumeRoot)
        {
            return isVolumeRoot ? Volume(entry.FullPath) : entry.IsDirectory ? Folder : FileType(entry.Name);
        }

        private static IImage Get(string key, string path, FileIconKind kind)
        {
            if (!Cache.TryGetValue(key, out IImage image))
            {
                image = ToBitmap(FileIcons.Read(path, kind, Size)) ?? Drawn(kind);
                Cache[key] = image;
            }

            return image;
        }

        private static Bitmap ToBitmap(IconPixels pixels)
        {
            if (pixels == null)
            {
                return null;
            }

            GCHandle handle = GCHandle.Alloc(pixels.Bgra, GCHandleType.Pinned);

            try
            {
                return new Bitmap(
                    PixelFormat.Bgra8888,
                    AlphaFormat.Premul,
                    handle.AddrOfPinnedObject(),
                    new PixelSize(pixels.Width, pixels.Height),
                    new Vector(96, 96),
                    pixels.Width * 4);
            }
            finally
            {
                handle.Free();
            }
        }

        // Generic icons: a yellow folder, a grey drive, a white page.
        private static IImage Drawn(FileIconKind kind)
        {
            DrawingGroup group = new DrawingGroup();

            void Add(Geometry geometry, Color fill, Color outline)
            {
                group.Children.Add(new GeometryDrawing
                {
                    Geometry = geometry,
                    Brush = new SolidColorBrush(fill),
                    Pen = new Pen(new SolidColorBrush(outline), 1),
                });
            }

            switch (kind)
            {
                case FileIconKind.Folder:
                case FileIconKind.OpenFolder:
                    Add(new RectangleGeometry(new Rect(1.5, 3.5, 6, 3)), Color.FromRgb(224, 168, 40), Color.FromRgb(190, 140, 30));
                    Add(new RectangleGeometry(new Rect(1.5, 5.5, 13, 8)), Color.FromRgb(248, 200, 72), Color.FromRgb(200, 150, 40));
                    break;

                case FileIconKind.Volume:
                    Add(new RectangleGeometry(new Rect(1.5, 5.5, 13, 6)), Color.FromRgb(200, 200, 200), Color.FromRgb(110, 110, 110));
                    Add(new RectangleGeometry(new Rect(11, 8, 2, 1)), Color.FromRgb(60, 200, 60), Color.FromRgb(60, 200, 60));
                    break;

                default:
                    Add(new RectangleGeometry(new Rect(3.5, 1.5, 9, 13)), Colors.White, Color.FromRgb(140, 140, 140));
                    break;
            }

            // Fixes the image to 16x16 even where the shapes do not reach.
            group.Children.Insert(0, new GeometryDrawing { Geometry = new RectangleGeometry(new Rect(0, 0, Size, Size)), Brush = Brushes.Transparent });
            return new DrawingImage(group);
        }
    }
}
