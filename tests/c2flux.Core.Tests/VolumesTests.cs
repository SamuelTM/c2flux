using System;
using System.IO;
using System.Linq;
using Xunit;

namespace c2flux.Core.Tests
{
    public class VolumesTests
    {
        private static bool Mac(string root, string format = "apfs") =>
            Volumes.IsUserVolume(AppPaths.Platform.MacOS, root, DriveType.Fixed, format);

        private static bool Linux(string root, DriveType type = DriveType.Fixed, string format = "ext4") =>
            Volumes.IsUserVolume(AppPaths.Platform.Linux, root, type, format);

        [Fact]
        public void MacOS_shows_the_startup_volume_and_volumes_under_Volumes()
        {
            Assert.True(Mac("/"));
            Assert.True(Mac("/Volumes/SSD"));
            Assert.False(Mac("/dev", "devfs"));
            Assert.False(Mac("/System/Volumes/Data"));
            Assert.False(Mac("/System/Volumes/VM"));
            Assert.False(Mac("/System/Volumes/Data/home", "autofs"));
        }

        [Fact]
        public void Linux_shows_disks_but_not_virtual_or_image_mounts()
        {
            Assert.True(Linux("/"));
            Assert.True(Linux("/home"));
            Assert.True(Linux("/media/user/USB", DriveType.Removable, "vfat"));
            Assert.True(Linux("/mnt/share", DriveType.Network, "nfs4"));
            Assert.False(Linux("/proc", DriveType.Ram, "proc"));
            Assert.False(Linux("/dev/shm", DriveType.Ram, "tmpfs"));
            Assert.False(Linux("/run/user/1000", DriveType.Fixed, "tmpfs"));
            Assert.False(Linux("/snap/core22/1380", DriveType.Fixed, "squashfs"));
            Assert.False(Linux("/var/lib/docker/overlay2/x/merged", DriveType.Fixed, "overlay"));
        }

        [Fact]
        public void Listing_the_volumes_of_this_machine_works()
        {
            VolumeInfo[] volumes = Volumes.List().ToArray();

            Assert.NotEmpty(volumes);
            Assert.All(volumes, volume =>
            {
                Assert.False(string.IsNullOrEmpty(volume.Label));
                Assert.True(volume.TotalBytes > 0, volume.RootPath);
                Assert.InRange(volume.FreeBytes, 0, volume.TotalBytes);
            });

            if (OperatingSystem.IsMacOS())
            {
                Assert.Contains(volumes, volume => volume.RootPath == "/");
                Assert.DoesNotContain(volumes, volume => volume.RootPath.StartsWith("/System/Volumes", StringComparison.Ordinal));
            }
        }
    }
}
