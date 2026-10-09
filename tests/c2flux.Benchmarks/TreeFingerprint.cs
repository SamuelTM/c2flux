using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace c2flux.Benchmarks
{
    // Summary of a scan result: counts, sizes and a SHA-256 over every entry
    // (relative path, kind, size, last write time). Two scans with the same
    // digest produced identical trees.
    internal sealed class TreeFingerprint
    {
        public long Directories { get; set; }
        public long Files { get; set; }
        public long RootSizeBytes { get; set; }
        public long FileBytes { get; set; }
        public long AllFilesCount { get; set; }
        public int MaxDepth { get; set; }
        public string Digest { get; set; }

        public static TreeFingerprint Compute(object rootEntry)
        {
            if (rootEntry == null)
            {
                throw new InvalidOperationException("The scanner returned no root entry.");
            }

            EntryAccessor accessor = new EntryAccessor(rootEntry.GetType());
            TreeFingerprint fingerprint = new TreeFingerprint
            {
                RootSizeBytes = accessor.GetSize(rootEntry),
                AllFilesCount = accessor.GetAllFilesCount(rootEntry),
            };

            List<string> lines = new List<string>();
            Stack<(object Entry, string Path, int Depth)> pending = new Stack<(object, string, int)>();
            pending.Push((rootEntry, string.Empty, 0));

            while (pending.Count > 0)
            {
                (object entry, string path, int depth) = pending.Pop();
                fingerprint.MaxDepth = Math.Max(fingerprint.MaxDepth, depth);

                foreach (object child in accessor.GetChildren(entry))
                {
                    string childPath = path + "/" + accessor.GetName(child);
                    long size = accessor.GetSize(child);
                    long lastWriteTicks = accessor.GetLastWriteTimeUtc(child).Ticks;

                    if (accessor.IsDirectory(child))
                    {
                        fingerprint.Directories++;
                        lines.Add(string.Concat("D|", childPath, "|", size.ToString(), "|", lastWriteTicks.ToString()));
                        pending.Push((child, childPath, depth + 1));
                    }
                    else
                    {
                        fingerprint.Files++;
                        fingerprint.FileBytes += size;
                        lines.Add(string.Concat("F|", childPath, "|", size.ToString(), "|", lastWriteTicks.ToString()));
                    }
                }
            }

            // Scanners may return children in different orders.
            lines.Sort(StringComparer.Ordinal);

            using (IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                foreach (string line in lines)
                {
                    hash.AppendData(Encoding.UTF8.GetBytes(line));
                    hash.AppendData(new byte[] { (byte)'\n' });
                }

                fingerprint.Digest = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            }

            return fingerprint;
        }

        // Reads FileSystemEntry members by name, since the type comes from the
        // app assembly loaded at runtime.
        private sealed class EntryAccessor
        {
            private readonly PropertyInfo _name;
            private readonly PropertyInfo _size;
            private readonly PropertyInfo _isDirectory;
            private readonly PropertyInfo _lastWriteTimeUtc;
            private readonly PropertyInfo _children;
            private readonly PropertyInfo _allFiles;

            public EntryAccessor(Type entryType)
            {
                _name = Require(entryType, "Name");
                _size = Require(entryType, "SizeBytes");
                _isDirectory = Require(entryType, "IsDirectory");
                _lastWriteTimeUtc = Require(entryType, "LastWriteTimeUtc");
                _children = Require(entryType, "Children");
                _allFiles = entryType.GetProperty("AllFiles");
            }

            public string GetName(object entry)
            {
                return (string)_name.GetValue(entry) ?? string.Empty;
            }

            public long GetSize(object entry)
            {
                return (long)_size.GetValue(entry);
            }

            public bool IsDirectory(object entry)
            {
                return (bool)_isDirectory.GetValue(entry);
            }

            public DateTime GetLastWriteTimeUtc(object entry)
            {
                return (DateTime)_lastWriteTimeUtc.GetValue(entry);
            }

            public IEnumerable GetChildren(object entry)
            {
                return (IEnumerable)_children.GetValue(entry) ?? Array.Empty<object>();
            }

            public long GetAllFilesCount(object entry)
            {
                return _allFiles?.GetValue(entry) is ICollection files ? files.Count : -1;
            }

            private static PropertyInfo Require(Type type, string name)
            {
                return type.GetProperty(name) ??
                    throw new InvalidOperationException(type.FullName + " has no property " + name + ".");
            }
        }
    }
}
