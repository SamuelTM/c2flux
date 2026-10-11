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

        [Fact]
        public void Treemap_tiles_fill_the_bounds_without_overlapping()
        {
            Treemap.TreemapNode root = new Treemap.TreemapNode(new FileSystemEntry { Name = "root", IsDirectory = true });
            root.Children.AddRange(new long[] { 600, 300, 50, 30, 20 }.Select(size =>
                new Treemap.TreemapNode(new FileSystemEntry { Name = "f" + size, SizeBytes = size })));
            Rect bounds = new Rect(0, 0, 400, 100);

            List<Treemap.LayoutItem> layout = Treemap.CreateSquarifiedLayout(root.Children, bounds);

            Assert.Equal(5, layout.Count);
            Assert.Equal(bounds.Width * bounds.Height, layout.Sum(item => item.Bounds.Width * item.Bounds.Height), 1);
            Assert.All(layout, item => Assert.True(bounds.Contains(item.Bounds)));

            for (int i = 0; i < layout.Count; i++)
            {
                for (int j = i + 1; j < layout.Count; j++)
                {
                    Rect overlap = layout[i].Bounds.Intersect(layout[j].Bounds);
                    Assert.True(overlap.Width * overlap.Height < 0.01, layout[i].Node.DisplayName + " overlaps " + layout[j].Node.DisplayName);
                }
            }

            // Area follows size: the 600-byte file gets 60 %.
            Assert.Equal(0.6 * 40000, layout[0].Bounds.Width * layout[0].Bounds.Height, 1);
        }

        [Fact]
        public void Treemap_groups_children_too_small_for_a_tile_as_other()
        {
            Treemap.TreemapNode root = new Treemap.TreemapNode(new FileSystemEntry { Name = "root", IsDirectory = true });
            root.Children.Add(new Treemap.TreemapNode(new FileSystemEntry { Name = "big", SizeBytes = 1_000_000 }));
            root.Children.AddRange(Enumerable.Range(0, 20).Select(index =>
                new Treemap.TreemapNode(new FileSystemEntry { Name = "tiny" + index, SizeBytes = 1 })));

            List<Treemap.TreemapNode> visible = Treemap.PrepareChildrenForLayout(root, new Rect(0, 0, 100, 100));

            // The 8 largest always stay; the other 13 tiny files become one tile.
            Assert.Equal(9, visible.Count);
            Assert.True(visible[^1].IsAggregate);
            Assert.Equal("Other (13)", visible[^1].DisplayName);
            Assert.Equal(13, Treemap.GetNodeSize(visible[^1]));
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

        [Fact]
        public void Header_clicks_cycle_ascending_descending_original_and_start_again()
        {
            DrawnTable<string> table = new DrawnTable<string>();
            TableColumn<string> column = new TableColumn<string>("Name", row => row);
            table.Columns.Add(column);
            table.SetItems(new[] { "b", "a", "c" });

            string Order() => string.Concat(table.SortedItems);
            table.ClickHeader(column);
            Assert.Equal("abc", Order());
            table.ClickHeader(column);
            Assert.Equal("cba", Order());
            table.ClickHeader(column);
            Assert.Equal("bac", Order());
            table.ClickHeader(column);
            Assert.Equal("abc", Order());
        }

        // Each folder's files are one range of the index: its count and its
        // largest files match a walk of its subtree.
        [Fact]
        public void Entry_index_counts_and_ranks_the_files_of_each_folder()
        {
            FileSystemEntry Folder(string path, params FileSystemEntry[] children)
            {
                FileSystemEntry folder = new FileSystemEntry { Name = System.IO.Path.GetFileName(path), FullPath = path, IsDirectory = true };
                folder.Children.AddRange(children);
                return folder;
            }

            FileSystemEntry File(string path, long size) => new FileSystemEntry { Name = System.IO.Path.GetFileName(path), FullPath = path, SizeBytes = size };

            FileSystemEntry deep = Folder("/r/a/b", File("/r/a/b/1", 50), File("/r/a/b/2", 5));
            FileSystemEntry a = Folder("/r/a", File("/r/a/3", 30), deep, Folder("/r/a/empty"));
            FileSystemEntry c = Folder("/r/c", File("/r/c/4", 40), File("/r/c/5", 1));
            FileSystemEntry root = Folder("/r", a, File("/r/6", 10), c);
            root.AllFiles.AddRange(EntryTable.GetLargestFiles(root, 100));

            Assert.Equal(6, EntryIndex.FileCount(root, root));
            Assert.Equal(3, EntryIndex.FileCount(root, a));
            Assert.Equal(2, EntryIndex.FileCount(root, deep));
            Assert.Equal(2, EntryIndex.FileCount(root, c));

            foreach (FileSystemEntry folder in new[] { root, a, deep, c })
            {
                Assert.Equal(
                    EntryTable.GetLargestFiles(folder, 2).Select(file => file.FullPath),
                    EntryIndex.LargestFiles(root, folder, 2).Select(file => file.FullPath));
            }

            Assert.Equal(new[] { "/r/c/4", "/r/a/b/1" }, EntryIndex.LargestFiles(root, root, 2).Select(file => file.FullPath));
            Assert.Null(EntryIndex.LargestFiles(root, File("/elsewhere", 1), 2));
        }

        // Files in AllFiles but not yet in a folder's Children, refreshed
        // when AllFiles grows.
        [Fact]
        public void Pending_files_are_the_listed_files_not_yet_attached()
        {
            FileSystemEntry root = new FileSystemEntry { Name = "root", FullPath = "/root", IsDirectory = true };
            FileSystemEntry folder = new FileSystemEntry { Name = "a", FullPath = "/root/a", IsDirectory = true };
            FileSystemEntry attached = new FileSystemEntry { Name = "x", FullPath = "/root/a/x", SizeBytes = 1 };
            FileSystemEntry pending = new FileSystemEntry { Name = "y", FullPath = "/root/a/y", SizeBytes = 2 };
            root.Children.Add(folder);
            folder.Children.Add(attached);
            root.AllFiles.AddRange(new[] { attached, pending });

            Assert.Equal(new[] { pending }, EntryIndex.PendingFiles(root));
            Assert.Equal(1, EntryIndex.FileCount(root, root));
            Assert.Equal(1, EntryIndex.FileCount(root, folder));

            FileSystemEntry later = new FileSystemEntry { Name = "z", FullPath = "/root/z", SizeBytes = 3 };
            root.AllFiles.Add(later);
            Assert.Equal(new[] { pending, later }, EntryIndex.PendingFiles(root));

            folder.Children.Add(pending);
            root.AllFiles.Remove(later);
            Assert.Empty(EntryIndex.PendingFiles(root));
            Assert.Equal(2, EntryIndex.FileCount(root, root));
        }
    }
}
