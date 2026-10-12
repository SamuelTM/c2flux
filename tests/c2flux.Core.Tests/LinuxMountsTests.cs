using System.Linq;
using Xunit;

namespace c2flux.Core.Tests
{
    public class LinuxMountsTests
    {
        private static readonly string[] MountInfo =
        {
            "22 1 8:1 / / rw,relatime shared:1 - ext4 /dev/sda1 rw",
            "23 22 0:21 / /proc rw,nosuid shared:12 - proc proc rw",
            "24 22 0:22 / /sys rw,nosuid shared:7 - sysfs sysfs rw",
            "25 22 8:2 / /home rw,relatime shared:2 - ext4 /dev/sda2 rw",
            "26 25 0:45 / /home/user/My\\040Drive rw - fuse.rclone remote: rw",
            "27 22 0:23 / /run rw - tmpfs tmpfs rw",
        };

        [Fact]
        public void Parses_mount_points_and_unescapes_octal_sequences()
        {
            Assert.Equal(
                new[] { "/", "/proc", "/sys", "/home", "/home/user/My Drive", "/run" },
                LinuxMounts.Parse(MountInfo).ToArray());
        }

        [Fact]
        public void Scanning_root_skips_every_other_mount()
        {
            Assert.Equal(
                new[] { "/home", "/home/user/My Drive", "/proc", "/run", "/sys" },
                LinuxScanner.MountsBelow("/", LinuxMounts.Parse(MountInfo)).OrderBy(path => path));
        }

        [Fact]
        public void Scanning_a_mount_point_enters_it_but_not_mounts_below_it()
        {
            Assert.Equal(
                new[] { "/home/user/My Drive" },
                LinuxScanner.MountsBelow("/home/", LinuxMounts.Parse(MountInfo)));
            Assert.Empty(LinuxScanner.MountsBelow("/homes", LinuxMounts.Parse(MountInfo)));
        }

        // A sparse file counts what it occupies (as du shows it), not its
        // length: Docker.raw showed 228 GB for 3 GB on disk. Linux only.
        [Fact]
        public async System.Threading.Tasks.Task A_sparse_file_counts_what_it_occupies()
        {
            if (!System.OperatingSystem.IsLinux())
            {
                return;
            }

            string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "c2flux-sparse-" + System.Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(folder);

            try
            {
                using (System.IO.FileStream file = System.IO.File.Create(System.IO.Path.Combine(folder, "sparse.bin")))
                {
                    file.SetLength(64L * 1024 * 1024);
                    file.Position = 32L * 1024 * 1024;
                    file.WriteByte(1);
                }

                System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder, "dense.bin"), new byte[2 * 1024 * 1024]);
                FileSystemEntry root = await new LinuxScanner(new AppSettings()).ScanAsync(folder, null, System.Threading.CancellationToken.None, default);
                long Size(string name) => root.Children.Single(child => child.Name == name).SizeBytes;

                Assert.InRange(Size("sparse.bin"), 1, 1024 * 1024);
                Assert.Equal(2 * 1024 * 1024, Size("dense.bin"));
            }
            finally
            {
                System.IO.Directory.Delete(folder, recursive: true);
            }
        }
    }
}
