using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

namespace c2flux
{
    // One walk over a scanned tree, kept until the root's AllFiles grows, so
    // a selection in a finished scan walks nothing:
    // - the files in tree order, so each folder's files are one range of an
    //   array (its file count, its largest files);
    // - the files listed in AllFiles but not in any folder's Children (a
    //   scan in progress, or "Show files in tree" off), placed under their
    //   folder by path once.
    internal static class EntryIndex
    {
        private sealed record Snapshot(
            List<FileSystemEntry> Source,
            int Count,
            List<FileSystemEntry> Unplaced,
            Dictionary<FileSystemEntry, List<FileSystemEntry>> PendingByDirectory,
            Dictionary<FileSystemEntry, (int Start, int End)> Ranges,
            FileSystemEntry[] Files,
            long[] Sizes);

        private static readonly ConditionalWeakTable<FileSystemEntry, Snapshot> Cache = new ConditionalWeakTable<FileSystemEntry, Snapshot>();

        private static readonly List<FileSystemEntry> None = new List<FileSystemEntry>();

        // Listed files whose folder is not (yet) in the tree.
        public static List<FileSystemEntry> UnplacedFiles(FileSystemEntry root) => root == null ? None : Get(root).Unplaced;

        // Listed files in directory that are not in its Children.
        public static List<FileSystemEntry> PendingChildren(FileSystemEntry root, FileSystemEntry directory) =>
            root != null && Get(root).PendingByDirectory.TryGetValue(directory, out List<FileSystemEntry> files) ? files : None;

        // Files below directory, which lies in root's tree.
        public static int FileCount(FileSystemEntry root, FileSystemEntry directory) =>
            Get(root).Ranges.TryGetValue(directory, out (int Start, int End) range) ? range.End - range.Start : 0;

        // The count largest files below directory, largest last;
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

            Dictionary<FileSystemEntry, List<FileSystemEntry>> pendingByDirectory = new Dictionary<FileSystemEntry, List<FileSystemEntry>>(ReferenceEqualityComparer.Instance);
            List<FileSystemEntry> files = new List<FileSystemEntry>();
            Dictionary<FileSystemEntry, (int Start, int End)> ranges = Walk(root, files, pendingByDirectory);
            List<FileSystemEntry> unplaced = new List<FileSystemEntry>();

            // shortcut: as many files in the tree as listed ones counts as all
            // in the tree (true for every scanner); compare by reference if not.
            if (allFiles.Count != files.Count)
            {
                HashSet<FileSystemEntry> attached = new HashSet<FileSystemEntry>(files, ReferenceEqualityComparer.Instance);
                Dictionary<string, FileSystemEntry> directories = new Dictionary<string, FileSystemEntry>(
                    OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

                foreach (FileSystemEntry directory in ranges.Keys)
                {
                    if (!string.IsNullOrWhiteSpace(directory.FullPath))
                    {
                        directories[Key(directory.FullPath)] = directory;
                    }
                }

                foreach (FileSystemEntry file in allFiles)
                {
                    if (file == null || file.IsDirectory || attached.Contains(file))
                    {
                        continue;
                    }

                    string parent = string.IsNullOrWhiteSpace(file.FullPath) ? null : Path.GetDirectoryName(file.FullPath);

                    if (!string.IsNullOrWhiteSpace(parent) && directories.TryGetValue(Key(parent), out FileSystemEntry directory))
                    {
                        if (!pendingByDirectory.TryGetValue(directory, out List<FileSystemEntry> list))
                        {
                            pendingByDirectory[directory] = list = new List<FileSystemEntry>();
                        }

                        list.Add(file);
                    }
                    else
                    {
                        unplaced.Add(file);
                    }
                }

                // Again, with the placed files in their folders' ranges.
                if (pendingByDirectory.Count > 0)
                {
                    files.Clear();
                    ranges = Walk(root, files, pendingByDirectory);
                }
            }

            Snapshot snapshot = new Snapshot(source, allFiles.Count, unplaced, pendingByDirectory, ranges, files.ToArray(), files.Select(file => file.SizeBytes).ToArray());
            Cache.AddOrUpdate(root, snapshot);
            return snapshot;
        }

        private static string Key(string path) => Path.TrimEndingDirectorySeparator(EntryPaths.ToNativeSeparators(path));

        // Depth first, so a folder's files (and its placed pending ones) are
        // added between its start and its end.
        private static Dictionary<FileSystemEntry, (int Start, int End)> Walk(FileSystemEntry root, List<FileSystemEntry> files, Dictionary<FileSystemEntry, List<FileSystemEntry>> pendingByDirectory)
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

                if (pendingByDirectory.TryGetValue(entry, out List<FileSystemEntry> placed))
                {
                    files.AddRange(placed);
                }

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
