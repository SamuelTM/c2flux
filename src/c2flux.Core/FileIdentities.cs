using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace c2flux
{
    // Identity of the data behind a path: two paths with the same identity are
    // hard links to one file (or a link and its target), not duplicates.
    public readonly struct FileIdentity : IEquatable<FileIdentity>
    {
        public FileIdentity(ulong volumeSerialNumber, ulong fileIdLow, ulong fileIdHigh)
        {
            VolumeSerialNumber = volumeSerialNumber;
            FileIdLow = fileIdLow;
            FileIdHigh = fileIdHigh;
        }

        public ulong VolumeSerialNumber { get; }
        public ulong FileIdLow { get; }
        public ulong FileIdHigh { get; }

        public bool Equals(FileIdentity other)
        {
            return
                VolumeSerialNumber == other.VolumeSerialNumber &&
                FileIdLow == other.FileIdLow &&
                FileIdHigh == other.FileIdHigh;
        }

        public override bool Equals(object obj)
        {
            return obj is FileIdentity other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(VolumeSerialNumber, FileIdLow, FileIdHigh);
        }
    }

    // Reads a file's identity, following links. usn is the NTFS change journal
    // number (Windows only; hasUsn false elsewhere), used to reuse cached
    // hashes. Returns false when the file cannot be opened.
    public delegate bool FileIdentityReader(string path, out FileIdentity identity, out long usn, out bool hasUsn);

    // Where RedundancyAnalysisService gets file identities. Each platform
    // registers its reader at startup (the WinForms Program.Main registers the
    // Windows one). The default works everywhere but knows no file ids: it
    // uses the path, so hard links of one file show up as duplicates.
    public static class FileIdentities
    {
        public static FileIdentityReader Reader { get; set; } = ByPath;

        public static bool ByPath(string path, out FileIdentity identity, out long usn, out bool hasUsn)
        {
            usn = 0;
            hasUsn = false;

            if (!File.Exists(path))
            {
                identity = default;
                return false;
            }

            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path)));
            identity = new FileIdentity(0, BitConverter.ToUInt64(hash, 0), BitConverter.ToUInt64(hash, 8));
            return true;
        }
    }
}
