using System;
using System.Collections.Generic;

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
    }
}
