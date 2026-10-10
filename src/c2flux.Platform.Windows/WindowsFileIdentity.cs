using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace c2flux
{
    // FileIdentityReader for Windows: the 128-bit NTFS/ReFS file id and the
    // change journal number (USN). Moved from RedundancyAnalysisService.
    public static class WindowsFileIdentity
    {
        private const int FileIdInfoClass = 18;
        private const uint FsctlReadFileUsnData = 0x000900EB;

        public static bool TryRead(
            string path,
            out FileIdentity identity,
            out long usn,
            out bool hasUsn)
        {
            try
            {
                using SafeFileHandle handle =
                    File.OpenHandle(
                        path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite |
                            FileShare.Delete,
                        FileOptions.None);

                if (!GetFileInformationByHandleEx(
                        handle,
                        FileIdInfoClass,
                        out FileIdInfo fileIdInfo,
                        (uint)Marshal.SizeOf<FileIdInfo>()))
                {
                    identity = default;
                    usn = 0L;
                    hasUsn = false;
                    return false;
                }

                identity =
                    new FileIdentity(
                        fileIdInfo.VolumeSerialNumber,
                        fileIdInfo.FileId.Low,
                        fileIdInfo.FileId.High);

                usn = 0L;
                hasUsn =
                    TryGetFileUsn(
                        handle,
                        out usn);

                return true;
            }
            catch
            {
                identity = default;
                usn = 0L;
                hasUsn = false;
                return false;
            }
        }

        private static bool TryGetFileUsn(
            SafeFileHandle handle,
            out long usn)
        {
            ReadFileUsnData input =
                new ReadFileUsnData
                {
                    MinMajorVersion = 2,
                    MaxMajorVersion = 3
                };

            byte[] output =
                new byte[512];

            if (!DeviceIoControl(
                    handle,
                    FsctlReadFileUsnData,
                    ref input,
                    (uint)Marshal.SizeOf<ReadFileUsnData>(),
                    output,
                    (uint)output.Length,
                    out uint bytesReturned,
                    IntPtr.Zero) ||
                bytesReturned < 32)
            {
                usn = 0L;
                return false;
            }

            ushort majorVersion =
                BitConverter.ToUInt16(
                    output,
                    4);

            int usnOffset =
                majorVersion == 3
                    ? 40
                    : 24;

            if (bytesReturned <
                usnOffset + sizeof(long))
            {
                usn = 0L;
                return false;
            }

            usn =
                BitConverter.ToInt64(
                    output,
                    usnOffset);

            return true;
        }

        [DllImport(
            "kernel32.dll",
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeviceIoControl(
            SafeFileHandle deviceHandle,
            uint ioControlCode,
            ref ReadFileUsnData inputBuffer,
            uint inputBufferSize,
            [Out] byte[] outputBuffer,
            uint outputBufferSize,
            out uint bytesReturned,
            IntPtr overlapped);

        [StructLayout(LayoutKind.Sequential)]
        private struct ReadFileUsnData
        {
            public ushort MinMajorVersion;
            public ushort MaxMajorVersion;
        }

        [DllImport(
            "kernel32.dll",
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandleEx(
            SafeFileHandle fileHandle,
            int fileInformationClass,
            out FileIdInfo fileInformation,
            uint bufferSize);

        [StructLayout(LayoutKind.Sequential)]
        private struct FileIdInfo
        {
            public ulong VolumeSerialNumber;
            public FileId128 FileId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FileId128
        {
            public ulong Low;
            public ulong High;
        }
    }
}
