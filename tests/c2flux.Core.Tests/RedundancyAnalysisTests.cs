using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Xunit;

namespace c2flux.Core.Tests
{
    public class RedundancyAnalysisTests
    {
        // Duplicates are found on every OS; hard links of one file are one
        // physical copy, not duplicates (with a native identity reader).
        [Fact]
        public void Finds_duplicates_and_counts_hard_links_once()
        {
            FileIdentityReader previous = FileIdentities.Reader;
            FileIdentityReader native =
                OperatingSystem.IsMacOS() ? MacFileIdentity.TryRead :
                OperatingSystem.IsLinux() ? LinuxFileIdentity.TryRead :
                null;

            string directory = TestEnvironment.CreateDirectory("redundancy");
            byte[] content = new byte[64 * 1024];
            new Random(7).NextBytes(content);

            string a = Path.Combine(directory, "a.bin");
            string b = Path.Combine(directory, "b.bin");
            string unique = Path.Combine(directory, "unique.bin");
            File.WriteAllBytes(a, content);
            File.WriteAllBytes(b, content);
            content[0] ^= 0xFF;
            File.WriteAllBytes(unique, content);

            List<string> paths = new List<string> { a, b, unique };
            string link = Path.Combine(directory, "a-link.bin");

            if (native != null && link_(a, link) == 0)
            {
                paths.Add(link);
            }

            try
            {
                FileIdentities.Reader = native ?? FileIdentities.ByPath;

                IReadOnlyList<RedundancyAnalysisGroup> groups = RedundancyAnalysisService.Analyze(
                    paths.Select(path => new FileSystemEntry
                    {
                        Name = Path.GetFileName(path),
                        FullPath = path,
                        SizeBytes = new FileInfo(path).Length,
                    }).ToList(),
                    CancellationToken.None,
                    null,
                    null);

                RedundancyAnalysisGroup group = Assert.Single(groups);
                Assert.Equal(2, group.PhysicalCopyCount);
                Assert.DoesNotContain(unique, group.Locations);
                Assert.Contains(a, group.Locations);
                Assert.Contains(b, group.Locations);
            }
            finally
            {
                FileIdentities.Reader = previous;
            }
        }

        // Scanners give a sparse or compressed file what it occupies: copies
        // are compared by length, and the total is the space they really use.
        [Fact]
        public void Finds_duplicates_whose_scanned_size_is_the_occupied_space()
        {
            string directory = TestEnvironment.CreateDirectory("redundancy-occupied");
            byte[] content = new byte[64 * 1024];
            new Random(11).NextBytes(content);
            string a = Path.Combine(directory, "a.bin");
            string b = Path.Combine(directory, "b.bin");
            string other = Path.Combine(directory, "other.bin");
            File.WriteAllBytes(a, content);
            File.WriteAllBytes(b, content);
            File.WriteAllBytes(other, new byte[70 * 1024]);

            FileSystemEntry Entry(string path, long occupied) => new FileSystemEntry { Name = Path.GetFileName(path), FullPath = path, SizeBytes = occupied };

            RedundancyAnalysisGroup group = Assert.Single(RedundancyAnalysisService.Analyze(
                new[] { Entry(a, 4096), Entry(b, 8192), Entry(other, 4096) },
                CancellationToken.None,
                null,
                null));

            Assert.Equal(2, group.PhysicalCopyCount);
            Assert.Equal(content.Length, group.SizeBytes);
            Assert.Equal(4096 + 8192, group.TotalSizeBytes);
            Assert.DoesNotContain(other, group.Locations);
        }

        // A size that is no whole number of 512-byte units is the length: no
        // file system call per file.
        [Fact]
        public void Only_sizes_in_512_byte_units_ask_the_file_system()
        {
            string missing = Path.Combine(Path.GetTempPath(), "c2flux-missing-" + Guid.NewGuid().ToString("N"));

            Assert.Equal(1000, RedundancyAnalysisService.GetLength(new FileSystemEntry { FullPath = missing, SizeBytes = 1000 }));
            Assert.Equal(1024, RedundancyAnalysisService.GetLength(new FileSystemEntry { FullPath = missing, SizeBytes = 1024 }));
        }

        // Clones are still copies to list, but their blocks count once.
        [Fact]
        public void Clones_count_once_in_the_total()
        {
            string directory = TestEnvironment.CreateDirectory("redundancy-clones");
            byte[] content = new byte[64 * 1024];
            new Random(13).NextBytes(content);
            string original = Path.Combine(directory, "original.bin");
            string clone = Path.Combine(directory, "clone.bin");
            string copy = Path.Combine(directory, "copy.bin");
            File.WriteAllBytes(original, content);
            File.WriteAllBytes(copy, content);

            FileCloneIdReader previous = FileIdentities.CloneIdReader;

            if (OperatingSystem.IsMacOS() && clonefile(original, clone, 0) == 0)
            {
                FileIdentities.CloneIdReader = MacFileIdentity.TryReadCloneId;
            }
            else
            {
                File.WriteAllBytes(clone, content);
                FileIdentities.CloneIdReader = (string path, out ulong cloneId) =>
                {
                    cloneId = path == copy ? 2UL : 1UL;
                    return true;
                };
            }

            try
            {
                FileSystemEntry Entry(string path) => new FileSystemEntry { Name = Path.GetFileName(path), FullPath = path, SizeBytes = content.Length };

                RedundancyAnalysisGroup group = Assert.Single(RedundancyAnalysisService.Analyze(
                    new[] { Entry(original), Entry(clone), Entry(copy) },
                    CancellationToken.None,
                    null,
                    null));

                Assert.Equal(3, group.PhysicalCopyCount);
                Assert.Equal(content.Length * 2L, group.TotalSizeBytes);
            }
            finally
            {
                FileIdentities.CloneIdReader = previous;
            }
        }

        [System.Runtime.InteropServices.DllImport("libc", SetLastError = true)]
        private static extern int clonefile(string source, string destination, uint flags);

        [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "link", SetLastError = true)]
        private static extern int link_(string existing, string created);
    }
}
