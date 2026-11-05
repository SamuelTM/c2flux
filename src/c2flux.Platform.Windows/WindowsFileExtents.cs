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
        private const long SparseLcn = -1;
        private const int HeaderSize = 16;
        private const int ExtentSize = 16;
        private const int ExtentsPerCall = 512;

        public static bool TryRead(string path, out IReadOnlyList<FileExtent> extents)
        {
            extents = Array.Empty<FileExtent>();
            List<FileExtent> found = new List<FileExtent>();
            byte[] buffer = new byte[HeaderSize + ExtentsPerCall * ExtentSize];
            char[] fileSystem = new char[32];

            try
            {
                using SafeFileHandle handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

                if (!GetVolumeInformationByHandleW(handle, null, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, fileSystem, (uint)fileSystem.Length) ||
                    new string(fileSystem).TrimEnd('\0') != "ReFS")
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
                            found.Add(new FileExtent((ulong)lcn, (ulong)(nextVcn - vcn)));
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
