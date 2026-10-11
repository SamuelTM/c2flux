using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace c2flux
{
    // One walk over a scanned tree, kept until the root's AllFiles grows, so
    // a selection in a finished scan walks nothing:
    // - the attached files in tree order, so each folder's files are one
    //   range of an array (its file count, its largest files);
    // - the files a scan in progress has listed in AllFiles but not yet
    //   attached to a folder's Children.
    internal static class EntryIndex
    {
        private sealed record Snapshot(
            List<FileSystemEntry> Source,
            int Count,
            List<FileSystemEntry> Pending,
            Dictionary<FileSystemEntry, (int Start, int End)> Ranges,
            FileSystemEntry[] Files,
            long[] Sizes);

        private static readonly ConditionalWeakTable<FileSystemEntry, Snapshot> Cache = new ConditionalWeakTable<FileSystemEntry, Snapshot>();

        // May still hold files attached since: callers skip those by path.
        public static List<FileSystemEntry> PendingFiles(FileSystemEntry root) => root == null ? new List<FileSystemEntry>() : Get(root).Pending;

        // Attached files below directory, which lies in root's tree.
        public static int FileCount(FileSystemEntry root, FileSystemEntry directory) =>
            Get(root).Ranges.TryGetValue(directory, out (int Start, int End) range) ? range.End - range.Start : 0;

        // The count largest attached files below directory, largest last;
        // null when directory is not in root's tree.
        public static List<FileSystemEntry> LargestFiles(FileSystemEntry root, FileSystemEntry directory, int count)
        {
            Snapshot snapshot = Get(root);

            if (!snapshot.Ranges.TryGetValue(directory, out (int Start, int End) range))
            {
                return null;
            }

            PriorityQueue<int, long> largest = new PriorityQueue<int, long>();
            long smallest = long.MinValue;

            for (int index = range.Start; index < range.End; index++)
            {
                long size = snapshot.Sizes[index];

                if (largest.Count < count)
                {
                    largest.Enqueue(index, size);
                    largest.TryPeek(out _, out smallest);
                }
                else if (size > smallest)
                {
                    largest.EnqueueDequeue(index, size);
                    largest.TryPeek(out _, out smallest);
                }
            }

            List<FileSystemEntry> result = new List<FileSystemEntry>(largest.Count);

            while (largest.Count > 0)
            {
                result.Add(snapshot.Files[largest.Dequeue()]);
            }

            return result;
        }

        private static Snapshot Get(FileSystemEntry root)
        {
            List<FileSystemEntry> source = root.AllFiles;
            List<FileSystemEntry> allFiles;

            lock (source)
            {
                if (Cache.TryGetValue(root, out Snapshot cached) && ReferenceEquals(cached.Source, source) && cached.Count == source.Count)
                {
                    return cached;
                }

                allFiles = new List<FileSystemEntry>(source);
            }

            List<FileSystemEntry> files = new List<FileSystemEntry>();
            Dictionary<FileSystemEntry, (int Start, int End)> ranges = Walk(root, files);
            List<FileSystemEntry> pending = new List<FileSystemEntry>();

            // shortcut: as many attached files as listed ones counts as all
            // attached (true for every scanner); compare by reference if not.
            if (allFiles.Count != files.Count)
            {
                HashSet<FileSystemEntry> attached = new HashSet<FileSystemEntry>(files, ReferenceEqualityComparer.Instance);
                pending = allFiles.Where(file => file != null && !file.IsDirectory && !attached.Contains(file)).ToList();
            }

            Snapshot snapshot = new Snapshot(source, allFiles.Count, pending, ranges, files.ToArray(), files.Select(file => file.SizeBytes).ToArray());
            Cache.AddOrUpdate(root, snapshot);
            return snapshot;
        }

        // Depth first, so a folder's files are added between its start and
        // its end.
        private static Dictionary<FileSystemEntry, (int Start, int End)> Walk(FileSystemEntry root, List<FileSystemEntry> files)
        {
            Dictionary<FileSystemEntry, (int Start, int End)> ranges = new Dictionary<FileSystemEntry, (int Start, int End)>(ReferenceEqualityComparer.Instance);
            Stack<(FileSystemEntry Entry, int Start)> pending = new Stack<(FileSystemEntry Entry, int Start)>();
            pending.Push((root, -1));

            while (pending.Count > 0)
            {
                (FileSystemEntry entry, int start) = pending.Pop();

                if (!entry.IsDirectory)
                {
                    files.Add(entry);
                    continue;
                }

                if (start >= 0)
                {
                    ranges[entry] = (start, files.Count);
                    continue;
                }

                pending.Push((entry, files.Count));

                lock (entry.Children)
                {
                    foreach (FileSystemEntry child in entry.Children)
                    {
                        if (child != null)
                        {
                            pending.Push((child, -1));
                        }
                    }
                }
            }

            return ranges;
        }
    }
}
