using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace c2flux
{
    // Linux scan chain: LinuxScanner -> ManagedScanner.
    public static class LinuxScanners
    {
        public static ScannerPipeline CreatePipeline(AppSettings settings)
        {
            return new ScannerPipeline(Create(settings));
        }

        public static IReadOnlyList<IFileSystemScanner> Create(AppSettings settings)
        {
            return new IFileSystemScanner[]
            {
                new LinuxScanner(settings),
                new ManagedScanner(settings),
            };
        }
    }

    // ManagedScanner that stays on the root's file system: mount points below
    // the root (other disks, network shares, and the virtual /proc, /sys, /dev,
    // /run, cgroup and tracing file systems) are listed but not entered, like
    // du -x. Mounts are read once per scan from /proc/self/mountinfo.
    //
    // No native listing: measured on Ubuntu (CI), FileSystemEnumerable with
    // ManagedScanner's workers was already faster than du on /usr (2.1 s vs
    // 2.6 s, 629k files), so getdents64 + statx would not pay off.
    public sealed class LinuxScanner : IFileSystemScanner
    {
        private const string MountInfoPath = "/proc/self/mountinfo";

        private readonly ManagedScanner _engine;

        public LinuxScanner(AppSettings settings)
        {
            _engine = new ManagedScanner(settings, CreateReader);
        }

        public string Name => "LinuxScanner";

        public string StatusTextKey => "Status.ScanRunning";

        public string FailureAlertKey => "Alert.NativeScanUnavailable";

        public ScannerSupport GetSupport(string rootPath)
        {
            if (!OperatingSystem.IsLinux())
            {
                return ScannerSupport.NotSupported("Not Linux");
            }

            if (!File.Exists(MountInfoPath))
            {
                return ScannerSupport.NotSupported(MountInfoPath + " not available");
            }

            return _engine.GetSupport(rootPath);
        }

        public Task<FileSystemEntry> ScanAsync(
            string rootPath,
            IProgress<ScanProgress> progress,
            CancellationToken cancellationToken,
            PauseToken pauseToken)
        {
            return _engine.ScanAsync(rootPath, progress, cancellationToken, pauseToken);
        }

        private static DirectoryReader CreateReader(string rootPath)
        {
            HashSet<string> mountsToSkip = MountsBelow(rootPath, LinuxMounts.Parse(File.ReadAllLines(MountInfoPath)));

            return (directoryPath, entries) =>
            {
                if (mountsToSkip.Contains(directoryPath))
                {
                    return;
                }

                ManagedScanner.ReadWithFileSystemEnumerable(directoryPath, entries);
            };
        }

        // Mount points strictly below rootPath. The root's own mount is the one
        // that contains it, so it is never in the set.
        internal static HashSet<string> MountsBelow(string rootPath, IEnumerable<string> mountPoints)
        {
            string root = rootPath.Length > 1 ? rootPath.TrimEnd('/') : rootPath;
            string prefix = root == "/" ? "/" : root + "/";
            HashSet<string> result = new HashSet<string>(StringComparer.Ordinal);

            foreach (string mountPoint in mountPoints)
            {
                if (mountPoint.Length > prefix.Length && mountPoint.StartsWith(prefix, StringComparison.Ordinal))
                {
                    result.Add(mountPoint);
                }
            }

            return result;
        }
    }

    internal static class LinuxMounts
    {
        // Mount points from /proc/self/mountinfo lines, field 5 (proc(5)):
        //   36 35 98:0 /mnt1 /mnt/parent rw,noatime master:1 - ext3 /dev/root rw
        // Spaces, tabs, newlines and backslashes in paths are octal-escaped.
        public static IEnumerable<string> Parse(IEnumerable<string> lines)
        {
            foreach (string line in lines)
            {
                string[] fields = line.Split(' ');

                if (fields.Length > 4)
                {
                    yield return Unescape(fields[4]);
                }
            }
        }

        private static string Unescape(string value)
        {
            if (value.IndexOf('\\') < 0)
            {
                return value;
            }

            StringBuilder builder = new StringBuilder(value.Length);

            for (int index = 0; index < value.Length; index++)
            {
                if (value[index] == '\\' && IsOctal(value, index + 1))
                {
                    builder.Append((char)Convert.ToInt32(value.Substring(index + 1, 3), 8));
                    index += 3;
                }
                else
                {
                    builder.Append(value[index]);
                }
            }

            return builder.ToString();
        }

        private static bool IsOctal(string value, int start)
        {
            if (start + 3 > value.Length)
            {
                return false;
            }

            for (int index = start; index < start + 3; index++)
            {
                if (value[index] < '0' || value[index] > '7')
                {
                    return false;
                }
            }

            return true;
        }
    }
}
