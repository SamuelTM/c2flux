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

            FileExtentReader reader;

            if (OperatingSystem.IsMacOS() && clonefile(original, clone, 0) == 0)
            {
                reader = MacFileIdentity.TryReadExtents;
            }
            else
            {
                File.WriteAllBytes(clone, content);
                reader = FakeExtents(new Dictionary<string, FileExtent[]>
                {
                    [original] = new[] { new FileExtent(0, 100) },
                    [clone] = new[] { new FileExtent(0, 100) },
                    [copy] = new[] { new FileExtent(100, 100) },
                });
            }

            RedundancyAnalysisGroup group = AnalyzeWith(reader, Entries(content.Length, original, clone, copy));

            Assert.Equal(3, group.PhysicalCopyCount);
            Assert.Equal(content.Length * 2L, group.TotalSizeBytes);
        }

        // A file pays for the part of its extents no earlier copy holds, at
        // its own occupied size (compressed extents map more than they occupy).
        // A file whose extents are unknown counts in full.
        [Fact]
        public void Partly_shared_files_pay_for_what_they_do_not_share()
        {
            string directory = TestEnvironment.CreateDirectory("redundancy-extents");
            byte[] content = new byte[64 * 1024];
            new Random(17).NextBytes(content);
            string a = Path.Combine(directory, "a.bin");
            string b = Path.Combine(directory, "b.bin");
            string unknown = Path.Combine(directory, "unknown.bin");
            File.WriteAllBytes(a, content);
            File.WriteAllBytes(b, content);
            File.WriteAllBytes(unknown, content);

            FileExtentReader reader = FakeExtents(new Dictionary<string, FileExtent[]>
            {
                [a] = new[] { new FileExtent(200, 100), new FileExtent(0, 100) },
                [b] = new[] { new FileExtent(50, 100), new FileExtent(200, 100) },
            });

            RedundancyAnalysisGroup group = AnalyzeWith(reader, Entries(4096, a, b, unknown));

            // a pays all, b pays a quarter (50 of its 200 mapped units are new),
            // unknown pays all.
            Assert.Equal(4096 + 1024 + 4096, group.TotalSizeBytes);
        }

        // Real clones on the volumes CI prepares (XFS, Btrfs, ReFS). Rewriting
        // part of a clone leaves only that part unshared.
        [Fact]
        public void Clones_on_real_volumes_share_their_extents()
        {
            string directories = Environment.GetEnvironmentVariable("C2FLUX_CLONE_DIRS");

            if (string.IsNullOrEmpty(directories))
            {
                return;
            }

            const int Size = 1024 * 1024;
            const int Rewritten = 128 * 1024;
            byte[] content = new byte[Size];
            new Random(19).NextBytes(content);
            FileIdentityReader previous = FileIdentities.Reader;
            FileIdentities.Reader = NativeIdentityReader();

            try
            {
                foreach (string root in directories.Split(Path.PathSeparator))
                {
                    string directory = Path.Combine(root, "c2flux-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(directory);
                    string original = Path.Combine(directory, "original.bin");
                    string clone = Path.Combine(directory, "clone.bin");
                    string copy = Path.Combine(directory, "copy.bin");
                    WriteToDisk(original, content, 0, Size);
                    WriteToDisk(copy, content, 0, Size);
                    CloneFile(original, clone, Size);

                    Assert.Equal(Size, AnalyzeWith(NativeExtentReader(), Entries(Size, original, clone)).TotalSizeBytes);
                    Assert.Equal(Size * 2L, AnalyzeWith(NativeExtentReader(), Entries(Size, original, copy)).TotalSizeBytes);

                    WriteToDisk(clone, content, 0, Rewritten);

                    // At least what was rewritten: a file system may copy a little more.
                    Assert.InRange(AnalyzeWith(NativeExtentReader(), Entries(Size, original, clone)).TotalSizeBytes, Size + Rewritten, Size + 2L * Rewritten);
                }
            }
            finally
            {
                FileIdentities.Reader = previous;
            }
        }

        // Plain files on the system volume share nothing with anyone.
        [Fact]
        public void Files_that_share_nothing_have_no_extents_to_compare()
        {
            if (OperatingSystem.IsMacOS())
            {
                return;
            }

            string path = Path.Combine(TestEnvironment.CreateDirectory("redundancy-plain"), "plain.bin");
            WriteToDisk(path, new byte[64 * 1024], 0, 64 * 1024);

            Assert.False(NativeExtentReader()(path, out _));
        }

        private static FileExtentReader FakeExtents(Dictionary<string, FileExtent[]> map)
        {
            return (string path, out IReadOnlyList<FileExtent> extents) =>
            {
                bool known = map.TryGetValue(path, out FileExtent[] found);
                extents = found;
                return known;
            };
        }

        private static FileExtentReader NativeExtentReader() =>
            OperatingSystem.IsWindows() ? WindowsFileExtents.TryRead : LinuxFileExtents.TryRead;

        private static FileIdentityReader NativeIdentityReader() =>
            OperatingSystem.IsWindows() ? WindowsFileIdentity.TryRead : LinuxFileIdentity.TryRead;

        private static FileSystemEntry[] Entries(long occupied, params string[] paths) =>
            paths.Select(path => new FileSystemEntry { Name = Path.GetFileName(path), FullPath = path, SizeBytes = occupied }).ToArray();

        private static RedundancyAnalysisGroup AnalyzeWith(FileExtentReader reader, FileSystemEntry[] entries)
        {
            FileExtentReader previous = FileIdentities.ExtentReader;
            FileIdentities.ExtentReader = reader;

            try
            {
                return Assert.Single(RedundancyAnalysisService.Analyze(entries, CancellationToken.None, null, null));
            }
            finally
            {
                FileIdentities.ExtentReader = previous;
            }
        }

        // Overwrites the start of the file and puts the blocks on disk.
        private static void WriteToDisk(string path, byte[] content, int offset, int count)
        {
            using FileStream stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write);
            stream.Write(content, offset, count);
            stream.Flush(true);
        }

        private static void CloneFile(string source, string destination, long length)
        {
            using FileStream from = new FileStream(source, FileMode.Open, FileAccess.Read);
            using FileStream to = new FileStream(destination, FileMode.CreateNew, FileAccess.ReadWrite);

            if (OperatingSystem.IsWindows())
            {
                to.SetLength(length);
                DuplicateExtentsData data = new DuplicateExtentsData { Source = from.SafeFileHandle.DangerousGetHandle(), ByteCount = length };
                Assert.True(DeviceIoControl(to.SafeFileHandle, FsctlDuplicateExtentsToFile, ref data, System.Runtime.InteropServices.Marshal.SizeOf<DuplicateExtentsData>(), IntPtr.Zero, 0, out _, IntPtr.Zero), "FSCTL_DUPLICATE_EXTENTS_TO_FILE failed");
            }
            else
            {
                Assert.Equal(0, ioctl((int)to.SafeFileHandle.DangerousGetHandle(), Ficlone, from.SafeFileHandle.DangerousGetHandle()));
            }
        }

        private const uint FsctlDuplicateExtentsToFile = 0x00098344;
        private const ulong Ficlone = 0x40049409;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct DuplicateExtentsData
        {
            public IntPtr Source;
            public long SourceOffset;
            public long TargetOffset;
            public long ByteCount;
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(Microsoft.Win32.SafeHandles.SafeFileHandle device, uint code, ref DuplicateExtentsData input, int inputSize, IntPtr output, int outputSize, out int returned, IntPtr overlapped);

        [System.Runtime.InteropServices.DllImport("libc", SetLastError = true)]
        private static extern int ioctl(int fd, ulong request, IntPtr argument);

        [System.Runtime.InteropServices.DllImport("libc", SetLastError = true)]
        private static extern int clonefile(string source, string destination, uint flags);

        [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "link", SetLastError = true)]
        private static extern int link_(string existing, string created);
    }
}
