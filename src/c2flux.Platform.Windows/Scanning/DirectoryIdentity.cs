using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace c2flux
{
    // Identity of a directory (volume + file id), used to detect a directory
    // reached twice through junctions or links. DirectoryScanner and
    // NtQueryDirectoryScanner had identical private copies of this code.
    internal readonly struct DirectoryIdentity
    {
        private const int FileIdInfoClass = 18;

        public DirectoryIdentity(
            bool hasExtendedId,
            ulong extendedVolumeSerialNumber,
            ulong extendedFileIdLow,
            ulong extendedFileIdHigh,
            bool hasLegacyId,
            uint legacyVolumeSerialNumber,
            ulong legacyFileId)
        {
            HasExtendedId = hasExtendedId;
            ExtendedVolumeSerialNumber = extendedVolumeSerialNumber;
            ExtendedFileIdLow = extendedFileIdLow;
            ExtendedFileIdHigh = extendedFileIdHigh;
            HasLegacyId = hasLegacyId;
            LegacyVolumeSerialNumber = legacyVolumeSerialNumber;
            LegacyFileId = legacyFileId;
        }

        public bool HasExtendedId { get; }
        public ulong ExtendedVolumeSerialNumber { get; }
        public ulong ExtendedFileIdLow { get; }
        public ulong ExtendedFileIdHigh { get; }
        public bool HasLegacyId { get; }
        public uint LegacyVolumeSerialNumber { get; }
        public ulong LegacyFileId { get; }

        public bool Matches(DirectoryIdentity other)
        {
            if (HasExtendedId && other.HasExtendedId)
            {
                return ExtendedVolumeSerialNumber == other.ExtendedVolumeSerialNumber &&
                    ExtendedFileIdLow == other.ExtendedFileIdLow &&
                    ExtendedFileIdHigh == other.ExtendedFileIdHigh;
            }

            if (HasLegacyId && other.HasLegacyId)
            {
                return LegacyVolumeSerialNumber == other.LegacyVolumeSerialNumber &&
                    LegacyFileId == other.LegacyFileId;
            }

            return false;
        }

        // Reads the identity of an open directory: the 128-bit file id when
        // available (ReFS, newer NTFS), the legacy 64-bit one otherwise.
        public static bool TryRead(
            SafeFileHandle directoryHandle,
            out DirectoryIdentity directoryIdentity)
        {
            directoryIdentity = default;

            FILE_ID_INFO fileIdInfo;
            bool hasExtendedId =
                GetFileInformationByHandleEx(
                    directoryHandle,
                    FileIdInfoClass,
                    out fileIdInfo,
                    (uint)Marshal.SizeOf(typeof(FILE_ID_INFO))) &&
                (fileIdInfo.FileId.LowPart != 0 ||
                 fileIdInfo.FileId.HighPart != 0);

            bool hasLegacyId =
                GetFileInformationByHandle(
                    directoryHandle,
                    out BY_HANDLE_FILE_INFORMATION fileInformation);

            if (!hasExtendedId && !hasLegacyId)
                return false;

            ulong legacyFileId = hasLegacyId
                ? ((ulong)fileInformation.nFileIndexHigh << 32) |
                    fileInformation.nFileIndexLow
                : 0;

            directoryIdentity = new DirectoryIdentity(
                hasExtendedId,
                hasExtendedId ? fileIdInfo.VolumeSerialNumber : 0,
                hasExtendedId ? fileIdInfo.FileId.LowPart : 0,
                hasExtendedId ? fileIdInfo.FileId.HighPart : 0,
                hasLegacyId,
                hasLegacyId ? fileInformation.dwVolumeSerialNumber : 0,
                legacyFileId);

            return true;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetFileInformationByHandleEx(
            SafeFileHandle hFile,
            int fileInformationClass,
            out FILE_ID_INFO lpFileInformation,
            uint dwBufferSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetFileInformationByHandle(
            SafeFileHandle hFile,
            out BY_HANDLE_FILE_INFORMATION lpFileInformation);

        [StructLayout(LayoutKind.Sequential)]
        private struct FILE_ID_INFO
        {
            public ulong VolumeSerialNumber;
            public FILE_ID_128 FileId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FILE_ID_128
        {
            public ulong LowPart;
            public ulong HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BY_HANDLE_FILE_INFORMATION
        {
            public FileAttributes dwFileAttributes;
            public FILETIME ftCreationTime;
            public FILETIME ftLastAccessTime;
            public FILETIME ftLastWriteTime;
            public uint dwVolumeSerialNumber;
            public uint nFileSizeHigh;
            public uint nFileSizeLow;
            public uint nNumberOfLinks;
            public uint nFileIndexHigh;
            public uint nFileIndexLow;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FILETIME
        {
            public uint dwLowDateTime;
            public uint dwHighDateTime;
        }
    }
}
