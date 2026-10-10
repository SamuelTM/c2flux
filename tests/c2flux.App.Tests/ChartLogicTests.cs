using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Xunit;

namespace c2flux.AppTests
{
    public class ChartLogicTests
    {
        [Fact]
        public void Pie_keeps_the_ten_largest_and_sums_the_rest_as_other()
        {
            FileSystemEntry root = new FileSystemEntry { Name = "root", IsDirectory = true };
            root.Children.AddRange(Enumerable.Range(1, 12).Select(size => new FileSystemEntry { Name = "f" + size, SizeBytes = size }));
            root.Children.Add(new FileSystemEntry { Name = "empty", SizeBytes = 0 });

            List<PieChart.ChartItem> items = PieChart.CreateItems(root);

            Assert.Equal(11, items.Count);
            Assert.Equal(new long[] { 12, 11, 10, 9, 8, 7, 6, 5, 4, 3 }, items.Take(10).Select(item => item.SizeBytes));
            Assert.Null(items[10].Entry);
            Assert.Equal(1 + 2, items[10].SizeBytes);
        }

        // Measured in the WinForms tree bars over the dark background:
        // (84, 76, 162). GDI+ rounds through 8-bit linear tables, hence the
        // tolerance.
        [Fact]
        public void Translucent_fills_blend_like_gdi_plus()
        {
            Color blended = ChartColors.BlendLinear(Color.FromArgb(90, 130, 120, 255), Color.FromRgb(32, 32, 32));

            Assert.InRange(blended.R, 82, 86);
            Assert.InRange(blended.G, 74, 78);
            Assert.InRange(blended.B, 160, 164);
        }

        [AvaloniaFact]
        public void Tree_selects_a_nested_file_by_expanding_its_folders()
        {
            FileSystemEntry root = ChartCaptures.LoadFixture();
            FileSystemEntry group = root.Children.Single(child => child.Name == "tree").Children[0];
            FileSystemEntry file = group.Children.First(child => child.IsDirectory).Children[0];
            EntryTree tree = new EntryTree();
            FileSystemEntry selected = null;
            tree.SelectedEntryChanged += entry => selected = entry;

            tree.SetRootEntry(root);
            Assert.Same(root, selected);

            Assert.True(tree.SelectEntry(file));
            Assert.Same(file, tree.SelectedEntry);
            Assert.Same(file, selected);
        }

        [Theory]
        [InlineData("/a/b", "/", true)]
        [InlineData("/a", "/a/", true)]
        [InlineData("/ab", "/a", false)]
        public void Tree_paths_below_a_root(string path, string root, bool expected)
        {
            Assert.Equal(expected, EntryTreeCanvas.IsSameOrDescendantPath(path, root));
        }

        [Theory]
        // Slice from 12 to 3 o'clock (GDI+ angles: -90 to 0).
        [InlineData(60, 10, true)]
        [InlineData(90, 40, true)]
        [InlineData(10, 60, false)]
        [InlineData(55, 45, true)]
        [InlineData(55, 55, false)]
        [InlineData(99, 1, false)]
        public void Pie_slice_hit_test_follows_gdi_angles(double x, double y, bool expected)
        {
            Rect bounds = new Rect(0, 0, 100, 100);

            Assert.Equal(expected, PieChart.SliceContains(bounds, -90, 90, new Point(x, y)));
        }
    }
}
