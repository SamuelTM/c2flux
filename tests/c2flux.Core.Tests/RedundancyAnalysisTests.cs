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

        [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "link", SetLastError = true)]
        private static extern int link_(string existing, string created);
    }
}
