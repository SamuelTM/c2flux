using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace c2flux.Core.Tests
{
    public class ScanHistoryTests
    {
        // Regression test for the bug found in phase 0.3: loading a saved scan
        // dropped every directory whose name sorts before its parent's name
        // (sparse inside tree, dir-00067 inside group-000), with everything
        // below it, so comparisons missed new, changed and deleted files.
        [Fact]
        public void Saved_scans_load_back_complete_and_compare_correctly()
        {
            ScanHistoryService.ConfigureDatabasePath(
                Path.Combine(TestEnvironment.CreateDirectory("history"), "scan_history.db"));
            string root = TestEnvironment.CreateDirectory("scanned-root");

            FileSystemEntry first = Tree(root, new Dictionary<string, long>
            {
                ["tree/sparse/sparse-64MiB.bin"] = 64L * 1024 * 1024,
                ["tree/bulk/group-000/dir-00067/file-0024.dat"] = 8_000_000,
                ["tree/wide/file-000000.dat"] = 5_920,
                ["tree/a-first.txt"] = 10,
            });

            FileSystemEntry second = Tree(root, new Dictionary<string, long>
            {
                ["tree/sparse/sparse-64MiB.bin"] = 68L * 1024 * 1024,
                ["tree/bulk/group-000/dir-00067/file-0024.dat"] = 8_000_000,
                ["tree/a-first.txt"] = 10,
                ["tree/added-after-first-scan.bin"] = 8L * 1024 * 1024,
            });

            ScanHistoryService.Save(first);
            ScanHistoryService.Save(second);

            List<ScanHistoryInfo> scans = ScanHistoryService.List()
                .Where(scan => string.Equals(scan.RootPath, root, StringComparison.OrdinalIgnoreCase))
                .OrderBy(scan => scan.CreatedUtc)
                .ToList();
            Assert.Equal(2, scans.Count);

            ScanHistorySnapshot loadedFirst = ScanHistoryService.Load(scans[0].FilePath);
            Assert.Equal(4, loadedFirst.RootEntry.AllFiles.Count);
            Assert.Contains(loadedFirst.RootEntry.AllFiles, file => file.Name == "sparse-64MiB.bin");
            Assert.Contains(loadedFirst.RootEntry.AllFiles, file => file.Name == "file-0024.dat");

            ScanHistoryComparisonResult result = new ScanHistoryCompareService().Compare(scans[0], scans[1], null);

            Assert.Equal(4, result.BaselineFileCount);
            Assert.Equal(4, result.CompareFileCount);
            Assert.Equal(new[] { "added-after-first-scan.bin" }, result.NewFiles.Select(change => Path.GetFileName(change.Path)));
            Assert.Equal(new[] { "file-000000.dat" }, result.DeletedFiles.Select(change => Path.GetFileName(change.Path)));

            ScanHistoryFileChange changed = Assert.Single(result.ChangedFiles);
            Assert.Equal("sparse-64MiB.bin", Path.GetFileName(changed.Path));
            Assert.Equal(4L * 1024 * 1024, changed.DeltaBytes);
        }

        // Builds a scan result the way the scanners do: directories with
        // summed sizes, files both in their parent's Children and in the
        // root's AllFiles.
        private static FileSystemEntry Tree(string root, Dictionary<string, long> files)
        {
            FileSystemEntry rootEntry = new FileSystemEntry
            {
                Name = Path.GetFileName(root),
                FullPath = root,
                IsDirectory = true,
                LastWriteTimeUtc = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            };

            Dictionary<string, FileSystemEntry> directories = new Dictionary<string, FileSystemEntry>
            {
                [string.Empty] = rootEntry,
            };

            foreach (KeyValuePair<string, long> file in files)
            {
                string[] parts = file.Key.Split('/');
                FileSystemEntry parent = rootEntry;
                string relative = string.Empty;

                for (int index = 0; index < parts.Length - 1; index++)
                {
                    relative = relative.Length == 0 ? parts[index] : relative + "/" + parts[index];

                    if (!directories.TryGetValue(relative, out FileSystemEntry directory))
                    {
                        directory = new FileSystemEntry
                        {
                            Name = parts[index],
                            FullPath = Path.Combine(parent.FullPath, parts[index]),
                            IsDirectory = true,
                            LastWriteTimeUtc = rootEntry.LastWriteTimeUtc,
                        };
                        parent.Children.Add(directory);
                        directories[relative] = directory;
                    }

                    parent = directory;
                }

                FileSystemEntry fileEntry = new FileSystemEntry
                {
                    Name = parts[parts.Length - 1],
                    FullPath = Path.Combine(parent.FullPath, parts[parts.Length - 1]),
                    SizeBytes = file.Value,
                    IsDirectory = false,
                    LastWriteTimeUtc = rootEntry.LastWriteTimeUtc.AddMinutes(file.Value % 1000),
                };
                parent.Children.Add(fileEntry);
                rootEntry.AllFiles.Add(fileEntry);
            }

            SumSizes(rootEntry);
            return rootEntry;
        }

        private static long SumSizes(FileSystemEntry entry)
        {
            if (!entry.IsDirectory)
            {
                return entry.SizeBytes;
            }

            entry.SizeBytes = entry.Children.Sum(SumSizes);
            return entry.SizeBytes;
        }
    }
}
