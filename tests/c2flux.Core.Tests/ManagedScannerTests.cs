using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace c2flux.Core.Tests
{
    public class ManagedScannerTests
    {
        private static readonly Dictionary<string, int> Files = new Dictionary<string, int>
        {
            // Directory names that sort before their parent's name: the case
            // the scan history once got wrong.
            ["tree/sparse/big.bin"] = 70_000,
            ["tree/bulk/group-000/dir-00067/file.dat"] = 30_000,
            ["tree/bulk/group-000/dir-00068/file.dat"] = 20_000,
            ["tree/a-first.txt"] = 10,
            ["tree/empty.txt"] = 0,
            ["top.bin"] = 5_000,
        };

        private static string CreateTree()
        {
            string root = TestEnvironment.CreateDirectory("managed-scan");

            foreach (KeyValuePair<string, int> file in Files)
            {
                string path = Path.Combine(root, file.Key.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, new byte[file.Value]);
            }

            Directory.CreateDirectory(Path.Combine(root, "tree", "empty-dir"));
            return root;
        }

        private static Task<FileSystemEntry> ScanAsync(string root, bool showFilesInTree = true)
        {
            ManagedScanner scanner = new ManagedScanner(new AppSettings { ShowFilesInTree = showFilesInTree });
            return scanner.ScanAsync(root, null, CancellationToken.None, default);
        }

        private static FileSystemEntry Find(FileSystemEntry root, params string[] names)
        {
            FileSystemEntry current = root;

            foreach (string name in names)
            {
                current = current.Children.Single(child => child.Name == name);
            }

            return current;
        }

        [Fact]
        public async Task Builds_the_full_tree_with_sizes_and_all_files()
        {
            string root = CreateTree();

            FileSystemEntry result = await ScanAsync(root);

            long total = Files.Values.Sum();
            Assert.Equal(total, result.SizeBytes);
            Assert.Equal(Files.Count, result.AllFiles.Count);
            Assert.Equal(70_000, Find(result, "tree", "sparse").SizeBytes);
            Assert.Equal(50_000, Find(result, "tree", "bulk", "group-000").SizeBytes);
            Assert.Equal(30_000, Find(result, "tree", "bulk", "group-000", "dir-00067", "file.dat").SizeBytes);
            Assert.Equal(0, Find(result, "tree", "empty-dir").SizeBytes);
            Assert.All(result.AllFiles, file => Assert.True(File.Exists(file.FullPath), file.FullPath));
        }

        [Fact]
        public async Task Children_are_sorted_largest_first_then_by_name()
        {
            string root = CreateTree();

            FileSystemEntry tree = Find(await ScanAsync(root), "tree");

            Assert.Equal(
                new[] { "sparse", "bulk", "a-first.txt", "empty-dir", "empty.txt" },
                tree.Children.Select(child => child.Name));
        }

        [Fact]
        public async Task Directory_sizes_are_right_when_files_are_hidden_from_the_tree()
        {
            string root = CreateTree();

            FileSystemEntry result = await ScanAsync(root, showFilesInTree: false);

            Assert.Equal(Files.Values.Sum(), result.SizeBytes);
            Assert.Equal(Files.Count, result.AllFiles.Count);
            Assert.All(result.Children, child => Assert.True(child.IsDirectory));
            Assert.Equal(120_010, Find(result, "tree").SizeBytes);
        }

        [Fact]
        public async Task Symbolic_links_are_listed_but_never_followed()
        {
            string root = CreateTree();
            string loop = Path.Combine(root, "tree", "loop");

            try
            {
                Directory.CreateSymbolicLink(loop, root);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                // Windows without the privilege to create symbolic links.
                return;
            }

            FileSystemEntry result = await ScanAsync(root);

            FileSystemEntry link = Find(result, "tree", "loop");
            Assert.False(link.IsDirectory);
            Assert.Equal(0, link.SizeBytes);
            Assert.Equal(Files.Values.Sum(), result.SizeBytes);
        }

        [Fact]
        public async Task Unreadable_directories_are_kept_empty_and_reported_as_skipped()
        {
            if (OperatingSystem.IsWindows() || Environment.UserName == "root")
            {
                // Permissions are set with Unix modes; root ignores them.
                return;
            }

            string root = CreateTree();
            string locked = Path.Combine(root, "tree", "locked");
            Directory.CreateDirectory(locked);
            File.WriteAllBytes(Path.Combine(locked, "hidden.bin"), new byte[1_000]);
            File.SetUnixFileMode(locked, UnixFileMode.None);

            try
            {
                List<ScanProgress> reports = new List<ScanProgress>();
                ManagedScanner scanner = new ManagedScanner(new AppSettings());
                FileSystemEntry result = await scanner.ScanAsync(root, new SynchronousProgress(reports), CancellationToken.None, default);

                Assert.Empty(Find(result, "tree", "locked").Children);
                Assert.Equal(Files.Values.Sum(), result.SizeBytes);
                ScanProgress last = reports.Last();
                Assert.Equal(1, last.SkippedDirectories);
                Assert.Contains(last.SkippedDirectoryDetails, detail => detail.StartsWith(locked, StringComparison.Ordinal));
            }
            finally
            {
                File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        [Fact]
        public async Task Cancellation_stops_the_scan()
        {
            string root = CreateTree();
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            ManagedScanner scanner = new ManagedScanner(new AppSettings());

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => scanner.ScanAsync(root, null, cancellation.Token, default));
        }

        // Progress<T> posts to a synchronization context; tests need the
        // reports in order and immediately.
        private sealed class SynchronousProgress : IProgress<ScanProgress>
        {
            private readonly List<ScanProgress> _reports;

            public SynchronousProgress(List<ScanProgress> reports)
            {
                _reports = reports;
            }

            public void Report(ScanProgress value)
            {
                lock (_reports)
                {
                    _reports.Add(value);
                }
            }
        }
    }
}
