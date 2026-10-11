using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace c2flux
{
    // Files a scan in progress has listed in the root's AllFiles but not yet
    // attached to a folder's Children. Found once per root and kept until
    // AllFiles grows, so a selection in a finished scan does not compare the
    // paths of every file on the drive.
    internal static class PendingFiles
    {
        private sealed record Snapshot(List<FileSystemEntry> Source, int Count, List<FileSystemEntry> Files);

        private static readonly ConditionalWeakTable<FileSystemEntry, Snapshot> Cache = new ConditionalWeakTable<FileSystemEntry, Snapshot>();

        // May still hold files attached since: callers skip those by path.
        public static List<FileSystemEntry> Of(FileSystemEntry root)
        {
            if (root == null)
            {
                return new List<FileSystemEntry>();
            }

            List<FileSystemEntry> source = root.AllFiles;
            List<FileSystemEntry> allFiles;

            lock (source)
            {
                if (Cache.TryGetValue(root, out Snapshot cached) && ReferenceEquals(cached.Source, source) && cached.Count == source.Count)
                {
                    return cached.Files;
                }

                allFiles = new List<FileSystemEntry>(source);
            }

            HashSet<FileSystemEntry> attached = AttachedFiles(root);
            List<FileSystemEntry> files = allFiles.Where(file => file != null && !file.IsDirectory && !attached.Contains(file)).ToList();
            Cache.AddOrUpdate(root, new Snapshot(source, allFiles.Count, files));
            return files;
        }

        // The files below entry (or entry itself) through Children.
        public static HashSet<FileSystemEntry> AttachedFiles(FileSystemEntry entry)
        {
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

                lock (current.Children)
                {
                    foreach (FileSystemEntry child in current.Children)
                    {
                        if (child != null)
                        {
                            pending.Push(child);
                        }
                    }
                }
            }

            return files;
        }
    }
}
