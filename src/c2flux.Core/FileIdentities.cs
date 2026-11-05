using System;
using System.Collections.Generic;
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

    // Where a file's data lies on its volume. The unit only has to be the same
    // for every file of a volume: equal ranges mean shared blocks.
    public readonly record struct FileExtent(ulong Start, ulong Length);

    // Reads a file's extents. Returns false when they are unknown or when the
    // file shares nothing, so that it counts in full. volume names the space
    // the extents belong to when it is wider than the file's own volume (Btrfs
    // subvolumes); 0 means the file's own volume.
    public delegate bool FileExtentReader(string path, out IReadOnlyList<FileExtent> extents, out ulong volume);

    // Where RedundancyAnalysisService gets file identities. Each platform
    // registers its reader at startup (the WinForms Program.Main registers the
    // Windows one). The default works everywhere but knows no file ids: it
    // uses the path, so hard links of one file show up as duplicates.
    public static class FileIdentities
    {
        public static FileIdentityReader Reader { get; set; } = ByPath;

        // Null until the platform registers its reader.
        public static FileExtentReader ExtentReader { get; set; }

        // True for a file system that can share blocks without showing which
        // (ZFS): a total there may be too high. Null until registered.
        public static Func<string, bool> HiddenCloneChecker { get; set; }

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
