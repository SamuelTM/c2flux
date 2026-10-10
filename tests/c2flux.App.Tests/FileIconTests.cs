using System;
using System.Linq;
using Xunit;

namespace c2flux.AppTests
{
    public class FileIconTests
    {
        // The native readers return real icons: some opaque pixels, some
        // transparent ones. Linux has no reader yet.
        [Theory]
        [InlineData(FileIconKind.Folder)]
        [InlineData(FileIconKind.File)]
        [InlineData(FileIconKind.Volume)]
        [InlineData(FileIconKind.OpenFolder)]
        public void Native_icons_have_shape(FileIconKind kind)
        {
            if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
            {
                return;
            }

            string path = kind == FileIconKind.Volume ? System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath(".")) : "report.pdf";
            IconPixels icon = FileIcons.Read(path, kind, 16);

            Assert.NotNull(icon);
            Assert.Equal(16 * 16 * 4, icon.Bgra.Length);
            byte[] alpha = Enumerable.Range(0, 256).Select(index => icon.Bgra[index * 4 + 3]).ToArray();
            Assert.Contains(alpha, value => value == 255);
            Assert.True(alpha.Distinct().Count() > 1, "the icon is a solid square");
        }
    }
}
