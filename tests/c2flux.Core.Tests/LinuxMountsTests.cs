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
    }
}
