using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace c2flux
{
    // FileExtentReader for ReFS, the Windows file system with block clones:
    // the file's clusters from FSCTL_GET_RETRIEVAL_POINTERS. Extents are in
    // clusters. Other file systems share no clusters and count in full.
    [SupportedOSPlatform("windows")]
    public static class WindowsFileExtents
    {
        private const uint FsctlGetRetrievalPointers = 0x00090073;
        private const int ErrorMoreData = 234;
        private const uint DriveRemote = 4;
        private const long SparseLcn = -1;
        private const int HeaderSize = 16;
        private const int ExtentSize = 16;
        private const int ExtentsPerCall = 512;

        public static bool TryRead(string path, out IReadOnlyList<FileExtent> extents, out ulong volume)
        {
            extents = Array.Empty<FileExtent>();
            volume = 0;
            List<FileExtent> found = new List<FileExtent>();
            byte[] buffer = new byte[HeaderSize + ExtentsPerCall * ExtentSize];

            try
            {
                using SafeFileHandle handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

                if (ReadFileSystemName(handle) != "ReFS")
                {
                    return false;
                }

                long startingVcn = 0;

                while (true)
                {
                    bool complete = DeviceIoControl(handle, FsctlGetRetrievalPointers, ref startingVcn, sizeof(long), buffer, (uint)buffer.Length, out _, IntPtr.Zero);

                    if (!complete && Marshal.GetLastPInvokeError() != ErrorMoreData)
                    {
                        // ERROR_HANDLE_EOF for a file without clusters included.
                        return false;
                    }

                    int extentCount = BitConverter.ToInt32(buffer, 0);
                    long vcn = BitConverter.ToInt64(buffer, 8);

                    for (int i = 0; i < extentCount; i++)
                    {
                        int offset = HeaderSize + i * ExtentSize;
                        long nextVcn = BitConverter.ToInt64(buffer, offset);
                        long lcn = BitConverter.ToInt64(buffer, offset + 8);

                        if (lcn != SparseLcn)
                        {
                            found.Add(new FileExtent((ulong)lcn, (ulong)(nextVcn - vcn), (ulong)vcn));
                        }

                        vcn = nextVcn;
                    }

                    if (complete || extentCount == 0)
                    {
                        extents = found;
                        return found.Count > 0;
                    }

                    startingVcn = vcn;
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return false;
            }
        }

        // FileLocationReader: the cluster of the file's first extent. A small
        // file kept inside the file table has none to go by.
        public static bool TryReadLocation(string path, out ulong location)
        {
            location = 0;
            byte[] buffer = new byte[HeaderSize + ExtentSize];
            long startingVcn = 0;

            try
            {
                using SafeFileHandle handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

                if (!DeviceIoControl(handle, FsctlGetRetrievalPointers, ref startingVcn, sizeof(long), buffer, (uint)buffer.Length, out _, IntPtr.Zero) &&
                    Marshal.GetLastPInvokeError() != ErrorMoreData)
                {
                    return false;
                }

                long lcn = BitConverter.ToInt64(buffer, HeaderSize + 8);

                if (BitConverter.ToInt32(buffer, 0) == 0 || lcn == SparseLcn)
                {
                    return false;
                }

                location = (ulong)lcn;
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return false;
            }
        }

        // For FileIdentities.HiddenCloneChecker: ZFS and the disks behind a
        // network share clone blocks without showing which. path is a file.
        public static bool MayHideClones(string path)
        {
            try
            {
                string root = Path.GetPathRoot(Path.GetFullPath(path));

                if (!string.IsNullOrEmpty(root) && GetDriveType(Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar) == DriveRemote)
                {
                    return true;
                }

                using SafeFileHandle handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                return ReadFileSystemName(handle) == "ZFS";
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static string ReadFileSystemName(SafeFileHandle handle)
        {
            char[] fileSystem = new char[32];

            return GetVolumeInformationByHandleW(handle, null, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, fileSystem, (uint)fileSystem.Length)
                ? new string(fileSystem).TrimEnd('\0')
                : null;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern uint GetDriveType(string rootPath);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeviceIoControl(
            SafeFileHandle device,
            uint controlCode,
            ref long input,
            uint inputSize,
            [Out] byte[] output,
            uint outputSize,
            out uint bytesReturned,
            IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetVolumeInformationByHandleW(
            SafeFileHandle file,
            char[] volumeName,
            uint volumeNameSize,
            IntPtr serialNumber,
            IntPtr maximumComponentLength,
            IntPtr flags,
            [Out] char[] fileSystemName,
            uint fileSystemNameSize);
    }
}
