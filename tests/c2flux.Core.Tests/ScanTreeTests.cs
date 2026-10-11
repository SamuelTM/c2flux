using System.IO;
using System.Linq;
using Xunit;

namespace c2flux.Core.Tests
{
    public class ScanTreeTests
    {
        private static FileSystemEntry Folder(string path, params FileSystemEntry[] children)
        {
            FileSystemEntry folder = new FileSystemEntry { Name = Path.GetFileName(path), FullPath = path, IsDirectory = true, SizeBytes = children.Sum(child => child.SizeBytes) };
            folder.Children.AddRange(children);
            return folder;
        }

        private static FileSystemEntry File(string path, long size) => new FileSystemEntry { Name = Path.GetFileName(path), FullPath = path, SizeBytes = size };

        private static string P(params string[] parts) => Path.Combine(new[] { Path.GetTempPath(), "r" }.Concat(parts).ToArray());

        // A deleted folder leaves its parent, its size leaves every folder
        // above, its files leave AllFiles (also those only listed there).
        [Fact]
        public void Removing_a_folder_updates_its_parents_and_the_file_list()
        {
            FileSystemEntry one = File(P("a", "b", "1"), 50);
            FileSystemEntry deep = Folder(P("a", "b"), one);
            FileSystemEntry three = File(P("a", "3"), 30);
            FileSystemEntry a = Folder(P("a"), three, deep);
            FileSystemEntry six = File(P("6"), 10);
            FileSystemEntry root = Folder(P(), a, six);
            FileSystemEntry listedOnly = File(P("a", "b", "listed"), 0);
            FileSystemEntry similarName = File(P("a", "bb"), 0);
            root.AllFiles.AddRange(new[] { one, three, six, listedOnly, similarName });

            Assert.Equal(new[] { root, a }, ScanTree.Remove(root, deep));

            Assert.Equal(new[] { three }, a.Children);
            Assert.Equal(30, a.SizeBytes);
            Assert.Equal(40, root.SizeBytes);
            Assert.Equal(new[] { three, six, similarName }, root.AllFiles);
        }

        [Fact]
        public void Removing_a_file_or_an_entry_outside_the_tree()
        {
            FileSystemEntry six = File(P("6"), 10);
            FileSystemEntry seven = File(P("7"), 5);
            FileSystemEntry root = Folder(P(), six, seven);
            root.AllFiles.AddRange(new[] { six, seven });

            Assert.Equal(new[] { root }, ScanTree.Remove(root, six));
            Assert.Equal(new[] { seven }, root.Children);
            Assert.Equal(new[] { seven }, root.AllFiles);
            Assert.Equal(5, root.SizeBytes);

            Assert.Empty(ScanTree.Remove(root, File(P("elsewhere"), 1)));
            Assert.Empty(ScanTree.Remove(root, root));
            Assert.Equal(5, root.SizeBytes);
        }
    }
}
