using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace c2flux
{
    [SupportedOSPlatform("windows")]
    public static class WindowsClusterSize
    {
        // For Volumes.ClusterSizeReader: sectors per cluster times bytes per
        // sector of the volume holding path.
        public static long Read(string path)
        {
            string root = Path.GetPathRoot(Path.GetFullPath(path));

            return !string.IsNullOrEmpty(root) &&
                GetDiskFreeSpace(root, out uint sectorsPerCluster, out uint bytesPerSector, out _, out _)
                    ? (long)sectorsPerCluster * bytesPerSector
                    : 0;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool GetDiskFreeSpace(
            string lpRootPathName,
            out uint lpSectorsPerCluster,
            out uint lpBytesPerSector,
            out uint lpNumberOfFreeClusters,
            out uint lpTotalNumberOfClusters);
    }
}
