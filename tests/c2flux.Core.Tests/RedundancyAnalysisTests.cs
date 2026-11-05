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

        // Only a file system that hides what it shares marks the total.
        [Fact]
        public void Hidden_clones_mark_the_total_as_possibly_overstated()
        {
            string directory = TestEnvironment.CreateDirectory("redundancy-hidden");
            string a = Path.Combine(directory, "a.bin");
            string b = Path.Combine(directory, "b.bin");
            File.WriteAllBytes(a, new byte[4096]);
            File.WriteAllBytes(b, new byte[4096]);
            FileExtentReader nothing = FakeExtents(new Dictionary<string, FileExtent[]>());
            Func<string, bool> previous = FileIdentities.HiddenCloneChecker;

            try
            {
                FileIdentities.HiddenCloneChecker = path => path == b;
                Assert.True(AnalyzeWith(nothing, Entries(4096, a, b)).TotalMayBeOverstated);

                FileIdentities.HiddenCloneChecker = path => false;
                Assert.False(AnalyzeWith(nothing, Entries(4096, a, b)).TotalMayBeOverstated);
            }
            finally
            {
                FileIdentities.HiddenCloneChecker = previous;
            }

            if (OperatingSystem.IsLinux())
            {
                Assert.False(LinuxFileExtents.MayHideClones(a));
            }
        }

        // Clones across Btrfs subvolumes: different device numbers, one block
        // space. C2FLUX_BTRFS_SUBVOLUMES comes from CI.
        [Fact]
        public void Clones_on_real_volumes_across_btrfs_subvolumes_share_their_extents()
        {
            string subvolumes = Environment.GetEnvironmentVariable("C2FLUX_BTRFS_SUBVOLUMES");

            if (string.IsNullOrEmpty(subvolumes))
            {
                return;
            }

            const int Size = 1024 * 1024;
            byte[] content = new byte[Size];
            new Random(23).NextBytes(content);
            string[] roots = subvolumes.Split(Path.PathSeparator);
            string original = Path.Combine(roots[0], Guid.NewGuid().ToString("N") + ".bin");
            string clone = Path.Combine(roots[1], Guid.NewGuid().ToString("N") + ".bin");
            WriteToDisk(original, content, 0, Size);
            CloneFile(original, clone, Size);
            FileIdentityReader previous = FileIdentities.Reader;
            FileIdentities.Reader = LinuxFileIdentity.TryRead;

            try
            {
                Assert.True(LinuxFileIdentity.TryRead(original, out FileIdentity first, out _, out _));
                Assert.True(LinuxFileIdentity.TryRead(clone, out FileIdentity second, out _, out _));
                Assert.NotEqual(first.VolumeSerialNumber, second.VolumeSerialNumber);
                Assert.Equal(Size, AnalyzeWith(LinuxFileExtents.TryRead, Entries(Size, original, clone)).TotalSizeBytes);
            }
            finally
            {
                FileIdentities.Reader = previous;
            }
        }

        // ZFS clones blocks but shows no extents, so the total stays in full
        // and carries the mark. This fails if ZFS ever exposes them: then the
        // mark can go. C2FLUX_ZFS_DIR comes from CI, which also checks that the
        // pool really saved space.
        [Fact]
        public void Clones_on_zfs_mark_the_total_as_possibly_overstated()
        {
            string directory = Environment.GetEnvironmentVariable("C2FLUX_ZFS_DIR");

            if (string.IsNullOrEmpty(directory))
            {
                return;
            }

            const int Size = 1024 * 1024;
            byte[] content = new byte[Size];
            new Random(29).NextBytes(content);
            string original = Path.Combine(directory, "original.bin");
            string clone = Path.Combine(directory, "clone.bin");
            string copy = Path.Combine(directory, "copy.bin");
            WriteToDisk(original, content, 0, Size);
            WriteToDisk(copy, content, 0, Size);
            FileIdentityReader previousReader = FileIdentities.Reader;
            Func<string, bool> previousChecker = FileIdentities.HiddenCloneChecker;
            FileIdentities.Reader = NativeIdentityReader();
            FileIdentities.HiddenCloneChecker = NativeHiddenCloneChecker();

            try
            {
                Assert.True(NativeHiddenCloneChecker()(original));

                RedundancyAnalysisGroup copies = AnalyzeWith(NativeExtentReader(), Entries(Size, original, copy));
                Assert.True(copies.TotalMayBeOverstated);
                Assert.Equal(Size * 2L, copies.TotalSizeBytes);

                // ZFS may refuse to clone blocks until their transaction group
                // reaches the disk, about 5 seconds at most.
                using (FileStream from = new FileStream(original, FileMode.Open, FileAccess.Read))
                using (FileStream to = new FileStream(clone, FileMode.CreateNew, FileAccess.ReadWrite))
                {
                    bool cloned = false;

                    for (int attempt = 0; attempt < 30 && !cloned; attempt++)
                    {
                        cloned = TryCloneOpened(from, to, Size);

                        if (!cloned)
                        {
                            Thread.Sleep(1000);
                        }
                    }

                    Assert.True(cloned, "Cloning failed: error " + System.Runtime.InteropServices.Marshal.GetLastPInvokeError());
                }

                RedundancyAnalysisGroup clones = AnalyzeWith(NativeExtentReader(), Entries(Size, original, clone));
                Assert.True(clones.TotalMayBeOverstated);
                Assert.Equal(Size * 2L, clones.TotalSizeBytes);
            }
            finally
            {
                FileIdentities.Reader = previousReader;
                FileIdentities.HiddenCloneChecker = previousChecker;
            }
        }

        // The disk behind a network share is out of sight, so totals carry the
        // mark. C2FLUX_NETWORK_DIRS comes from CI (NFS, SMB in loopback).
        [Fact]
        public void Network_shares_mark_the_total_as_possibly_overstated()
        {
            string directories = Environment.GetEnvironmentVariable("C2FLUX_NETWORK_DIRS");

            if (string.IsNullOrEmpty(directories))
            {
                return;
            }

            const int Size = 64 * 1024;
            byte[] content = new byte[Size];
            new Random(31).NextBytes(content);
            FileIdentityReader previousReader = FileIdentities.Reader;
            Func<string, bool> previousChecker = FileIdentities.HiddenCloneChecker;
            FileIdentities.Reader = NativeIdentityReader();
            FileIdentities.HiddenCloneChecker = NativeHiddenCloneChecker();

            try
            {
                foreach (string directory in directories.Split(Path.PathSeparator))
                {
                    string a = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".bin");
                    string b = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".bin");
                    WriteToDisk(a, content, 0, Size);
                    WriteToDisk(b, content, 0, Size);

                    Assert.True(NativeHiddenCloneChecker()(a), directory + " was not recognized");

                    RedundancyAnalysisGroup group = AnalyzeWith(NativeExtentReader(), Entries(Size, a, b));
                    Assert.True(group.TotalMayBeOverstated, directory);
                    Assert.Equal(Size * 2L, group.TotalSizeBytes);
                }
            }
            finally
            {
                FileIdentities.Reader = previousReader;
                FileIdentities.HiddenCloneChecker = previousChecker;
            }
        }

        // A local disk is what most totals are on: no mark.
        [Fact]
        public void Local_disks_do_not_mark_the_total()
        {
            string path = Path.Combine(TestEnvironment.CreateDirectory("redundancy-local"), "local.bin");
            File.WriteAllBytes(path, new byte[] { 1 });

            Assert.False(NativeHiddenCloneChecker()(path));
        }

        // No runner can mount ZFS on macOS (the kext needs approval and a
        // reboot), so this proves the read on two other file systems: the name
        // must follow the volume. The port registers itself as "zfs".
        [Fact]
        public void Reads_the_file_system_type_on_macos()
        {
            if (!OperatingSystem.IsMacOS())
            {
                return;
            }

            string directory = TestEnvironment.CreateDirectory("redundancy-fstype");
            string image = Path.Combine(directory, "hfs.dmg");
            string mountPoint = Path.Combine(directory, "mount");
            Directory.CreateDirectory(mountPoint);

            Assert.Equal("apfs", MacFileIdentity.ReadFileSystemType(directory));
            Assert.False(MacFileIdentity.MayHideClones(directory));
            Assert.Null(MacFileIdentity.ReadFileSystemType(Path.Combine(directory, "missing")));
            Assert.False(MacFileIdentity.MayHideClones(Path.Combine(directory, "missing")));

            Assert.Equal(0, Hdiutil("create", "-quiet", "-size", "8m", "-fs", "HFS+", "-volname", "c2flux", image));
            Assert.Equal(0, Hdiutil("attach", "-quiet", "-nobrowse", "-mountpoint", mountPoint, image));

            try
            {
                Assert.Equal("hfs", MacFileIdentity.ReadFileSystemType(mountPoint));
                Assert.False(MacFileIdentity.MayHideClones(mountPoint));
            }
            finally
            {
                Hdiutil("detach", "-quiet", "-force", mountPoint);
            }
        }

        private static int Hdiutil(params string[] arguments)
        {
            using System.Diagnostics.Process process = System.Diagnostics.Process.Start("hdiutil", arguments);
            process.WaitForExit();
            return process.ExitCode;
        }

        // A compressed APFS file has no block map: its clone id stands in.
        [Fact]
        public void Compressed_clones_count_once_on_apfs()
        {
            if (!OperatingSystem.IsMacOS())
            {
                return;
            }

            string directory = TestEnvironment.CreateDirectory("redundancy-compressed");
            string plain = Path.Combine(directory, "plain.txt");
            string compressed = Path.Combine(directory, "compressed.txt");
            string clone = Path.Combine(directory, "clone.txt");
            File.WriteAllText(plain, new string('x', 1024 * 1024));

            using (System.Diagnostics.Process ditto = System.Diagnostics.Process.Start("ditto", new[] { "--hfsCompression", plain, compressed }))
            {
                ditto.WaitForExit();
            }

            Assert.Equal(0, clonefile(compressed, clone, 0));

            FileIdentityReader previous = FileIdentities.Reader;
            FileIdentities.Reader = MacFileIdentity.TryRead;

            try
            {
                Assert.Equal(4096, AnalyzeWith(MacFileIdentity.TryReadExtents, Entries(4096, compressed, clone)).TotalSizeBytes);
            }
            finally
            {
                FileIdentities.Reader = previous;
            }
        }

        // Real clones: on the volumes CI prepares (XFS, Btrfs, ReFS), and on
        // APFS wherever the tests run. Rewriting part of a clone leaves only
        // that part unshared, even with the same content.
        [Fact]
        public void Clones_on_real_volumes_share_their_extents()
        {
            string directories = OperatingSystem.IsMacOS()
                ? TestEnvironment.CreateDirectory("redundancy-apfs")
                : Environment.GetEnvironmentVariable("C2FLUX_CLONE_DIRS");

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

            Assert.False(NativeExtentReader()(path, out _, out _));
        }

        private static FileExtentReader FakeExtents(Dictionary<string, FileExtent[]> map)
        {
            return (string path, out IReadOnlyList<FileExtent> extents, out ulong volume) =>
            {
                volume = 0;
                bool known = map.TryGetValue(path, out FileExtent[] found);
                extents = found;
                return known;
            };
        }

        private static FileExtentReader NativeExtentReader() =>
            OperatingSystem.IsWindows() ? WindowsFileExtents.TryRead : OperatingSystem.IsMacOS() ? MacFileIdentity.TryReadExtents : LinuxFileExtents.TryRead;

        private static FileIdentityReader NativeIdentityReader() =>
            OperatingSystem.IsWindows() ? WindowsFileIdentity.TryRead : OperatingSystem.IsMacOS() ? MacFileIdentity.TryRead : LinuxFileIdentity.TryRead;

        private static Func<string, bool> NativeHiddenCloneChecker() =>
            OperatingSystem.IsWindows() ? WindowsFileExtents.MayHideClones : OperatingSystem.IsMacOS() ? MacFileIdentity.MayHideClones : LinuxFileExtents.MayHideClones;

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
            if (OperatingSystem.IsMacOS())
            {
                Assert.Equal(0, clonefile(source, destination, 0));
                return;
            }

            using FileStream from = new FileStream(source, FileMode.Open, FileAccess.Read);
            using FileStream to = new FileStream(destination, FileMode.CreateNew, FileAccess.ReadWrite);
            Assert.True(TryCloneOpened(from, to, length), "Cloning failed: error " + System.Runtime.InteropServices.Marshal.GetLastPInvokeError());
        }

        private static bool TryCloneOpened(FileStream from, FileStream to, long length)
        {
            if (OperatingSystem.IsWindows())
            {
                to.SetLength(length);
                DuplicateExtentsData data = new DuplicateExtentsData { Source = from.SafeFileHandle.DangerousGetHandle(), ByteCount = length };
                return DeviceIoControl(to.SafeFileHandle, FsctlDuplicateExtentsToFile, ref data, System.Runtime.InteropServices.Marshal.SizeOf<DuplicateExtentsData>(), IntPtr.Zero, 0, out _, IntPtr.Zero);
            }

            return ioctl((int)to.SafeFileHandle.DangerousGetHandle(), Ficlone, from.SafeFileHandle.DangerousGetHandle()) == 0;
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
