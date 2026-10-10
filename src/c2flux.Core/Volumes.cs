using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace c2flux
{
    public sealed class VolumeInfo
    {
        public string RootPath { get; init; }
        public string Label { get; init; }
        public string FileSystem { get; init; }
        public long TotalBytes { get; init; }
        public long FreeBytes { get; init; }
    }

    // The drives and volumes a user would want to scan: the drive list and the
    // partition panel. .NET's DriveInfo works on every OS; on macOS and Linux
    // it also returns internal and virtual mounts, which are filtered out here.
    public static class Volumes
    {
        public static IReadOnlyList<VolumeInfo> List()
        {
            List<VolumeInfo> volumes = new List<VolumeInfo>();

            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (!drive.IsReady ||
                        !IsUserVolume(CurrentPlatform(), drive.RootDirectory.FullName, drive.DriveType, drive.DriveFormat))
                    {
                        continue;
                    }

                    volumes.Add(new VolumeInfo
                    {
                        RootPath = drive.RootDirectory.FullName,
                        Label = GetLabel(drive),
                        FileSystem = drive.DriveFormat,
                        TotalBytes = drive.TotalSize,
                        FreeBytes = drive.AvailableFreeSpace,
                    });
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                {
                    // A drive that disappears or cannot be queried is left out.
                }
            }

            return volumes;
        }

        private static readonly string[] LinuxSystemPrefixes = { "/proc", "/sys", "/dev", "/run", "/snap" };
        private static readonly string[] LinuxImageFormats = { "squashfs", "overlay", "autofs", "fuse.snapfuse" };

        // Pure, so the rules of every OS can be tested on any OS.
        internal static bool IsUserVolume(AppPaths.Platform platform, string rootPath, DriveType type, string format)
        {
            switch (platform)
            {
                case AppPaths.Platform.Windows:
                    return true;

                case AppPaths.Platform.MacOS:
                    // The startup volume and what is mounted under /Volumes
                    // (external disks, images, network shares); the other APFS
                    // volumes (VM, Preboot, Data, ...) and /dev are internal.
                    return rootPath == "/" ||
                        (rootPath.StartsWith("/Volumes/", StringComparison.Ordinal) && format != "autofs");

                default:
                    if (type == DriveType.Ram || type == DriveType.Unknown || type == DriveType.NoRootDirectory)
                    {
                        return false;
                    }

                    if (LinuxImageFormats.Contains(format, StringComparer.OrdinalIgnoreCase))
                    {
                        return false;
                    }

                    return !LinuxSystemPrefixes.Any(prefix =>
                        rootPath == prefix || rootPath.StartsWith(prefix + "/", StringComparison.Ordinal));
            }
        }

        private static string GetLabel(DriveInfo drive)
        {
            string root = drive.RootDirectory.FullName;

            if (OperatingSystem.IsWindows())
            {
                return drive.VolumeLabel;
            }

            // On macOS /Volumes holds a link to / named after the startup
            // volume ("Macintosh HD").
            if (OperatingSystem.IsMacOS() && root == "/")
            {
                return StartupVolumeName() ?? root;
            }

            string name = Path.GetFileName(root.TrimEnd('/'));
            return string.IsNullOrEmpty(name) ? root : name;
        }

        private static string StartupVolumeName()
        {
            try
            {
                foreach (string entry in Directory.EnumerateFileSystemEntries("/Volumes"))
                {
                    if (new DirectoryInfo(entry).LinkTarget == "/")
                    {
                        return Path.GetFileName(entry);
                    }
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
            }

            return null;
        }

        private static AppPaths.Platform CurrentPlatform()
        {
            return OperatingSystem.IsWindows() ? AppPaths.Platform.Windows
                : OperatingSystem.IsMacOS() ? AppPaths.Platform.MacOS
                : AppPaths.Platform.Linux;
        }
    }
}
