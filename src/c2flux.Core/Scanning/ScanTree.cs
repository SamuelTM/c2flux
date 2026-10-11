using System;
using System.Collections.Generic;
using System.IO;

namespace c2flux
{
    // Tree operations shared by scanners once a scan has finished.
    public static class ScanTree
    {
        // Largest first, then by name (case-insensitive), at every level.
        // Same order as DirectoryScanner (and the former NtfsMftScanner).
        public static void SortChildrenBySizeDescending(FileSystemEntry root)
        {
            Stack<(FileSystemEntry Entry, bool Visited)> stack = new Stack<(FileSystemEntry Entry, bool Visited)>();
            stack.Push((root, false));

            while (stack.Count > 0)
            {
                (FileSystemEntry Entry, bool Visited) current = stack.Pop();

                if (!current.Visited)
                {
                    stack.Push((current.Entry, true));

                    foreach (FileSystemEntry child in current.Entry.Children)
                    {
                        if (child.IsDirectory)
                        {
                            stack.Push((child, false));
                        }
                    }

                    continue;
                }

                current.Entry.Children.Sort((left, right) =>
                {
                    int sizeCompare = right.SizeBytes.CompareTo(left.SizeBytes);

                    if (sizeCompare != 0)
                    {
                        return sizeCompare;
                    }

                    return string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
                });
            }
        }

        // Takes entry (a file or folder of root's tree, not root) out of the
        // tree once it is gone from disk: out of its folder's Children, its
        // size out of every folder above it, its files out of root.AllFiles.
        // Returns the folders above it, root first; empty when entry is not
        // in the tree.
        public static List<FileSystemEntry> Remove(FileSystemEntry root, FileSystemEntry entry)
        {
            List<FileSystemEntry> ancestors = FindAncestors(root, entry);

            if (ancestors.Count == 0)
            {
                return ancestors;
            }

            FileSystemEntry parent = ancestors[^1];

            lock (parent.Children)
            {
                parent.Children.Remove(entry);
            }

            foreach (FileSystemEntry ancestor in ancestors)
            {
                ancestor.SizeBytes = Math.Max(0, ancestor.SizeBytes - entry.SizeBytes);
            }

            HashSet<FileSystemEntry> files = new HashSet<FileSystemEntry>(ReferenceEqualityComparer.Instance);
            Stack<FileSystemEntry> pending = new Stack<FileSystemEntry>();
            pending.Push(entry);

            while (pending.Count > 0)
            {
                FileSystemEntry current = pending.Pop();

                if (!current.IsDirectory)
                {
                    files.Add(current);
                    continue;
                }

                foreach (FileSystemEntry child in current.Children)
                {
                    pending.Push(child);
                }
            }

            // Files listed in AllFiles but not in Children ("Show files in
            // tree" off) are found by path.
            string prefix = entry.IsDirectory ? Path.TrimEndingDirectorySeparator(entry.FullPath) + Path.DirectorySeparatorChar : null;

            lock (root.AllFiles)
            {
                root.AllFiles.RemoveAll(file => file != null &&
                    (files.Contains(file) || (prefix != null && file.FullPath != null && file.FullPath.StartsWith(prefix, PathComparison))));
            }

            return ancestors;
        }

        private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        // The folders from root down to entry's folder, going down by path.
        private static List<FileSystemEntry> FindAncestors(FileSystemEntry root, FileSystemEntry entry)
        {
            List<FileSystemEntry> ancestors = new List<FileSystemEntry>();
            string path = entry?.FullPath;

            if (root == null || string.IsNullOrWhiteSpace(path) || ReferenceEquals(root, entry))
            {
                return ancestors;
            }

            FileSystemEntry current = root;

            while (current != null)
            {
                ancestors.Add(current);
                FileSystemEntry next = null;

                foreach (FileSystemEntry child in current.Children)
                {
                    if (ReferenceEquals(child, entry))
                    {
                        return ancestors;
                    }

                    if (next == null && child.IsDirectory && !string.IsNullOrWhiteSpace(child.FullPath) &&
                        path.StartsWith(Path.TrimEndingDirectorySeparator(child.FullPath) + Path.DirectorySeparatorChar, PathComparison))
                    {
                        next = child;
                    }
                }

                current = next;
            }

            ancestors.Clear();
            return ancestors;
        }
    }
}
