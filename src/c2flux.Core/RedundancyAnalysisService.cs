using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace c2flux
{
    internal sealed class RedundancyAnalysisGroup
    {
        public string Name { get; init; }
        public long SizeBytes { get; init; }
        public int PhysicalCopyCount { get; init; }
        public long TotalSizeBytes { get; init; }
        public bool TotalMayBeOverstated { get; init; }
        public IReadOnlyList<string> Locations { get; init; }
    }

    internal enum RedundancyAnalysisPhase
    {
        SizeGrouping,
        FirstBlock,
        LastBlock,
        FullHashLive,
        FullHashCache,
        FileIdentity,
        Cache,
        Completed
    }

    internal sealed class RedundancyAnalysisProgressInfo
    {
        public int Percentage { get; init; }
        public RedundancyAnalysisPhase Phase { get; init; }
    }

    internal static class RedundancyAnalysisService
    {
        private const int SampleBlockSize = 4 * 1024;
        private const int FirstChunkSize = 1024 * 1024;
        private const int ChunkGrowth = 8;
        private const int BufferSize = 1024 * 1024;
        private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(100);
        private static readonly TimeSpan CacheSaveInterval = TimeSpan.FromSeconds(30);

        // A write can stay in memory while the blocks still look shared.
        private static readonly TimeSpan CloneSettleTime = TimeSpan.FromMinutes(1);

        // For tests: every read of file content (path, offset, bytes).
        internal static Action<string, long, int> ReadObserver { get; set; }

        // For tests: every time the hash cache is written.
        internal static Action CacheSaveObserver { get; set; }

        public static IReadOnlyList<RedundancyAnalysisGroup> Analyze(
            IReadOnlyList<FileSystemEntry> files,
            CancellationToken cancellationToken,
            IProgress<RedundancyAnalysisGroup> progress,
            IProgress<RedundancyAnalysisProgressInfo> analysisProgress)
        {
            if (files == null)
                throw new ArgumentNullException(nameof(files));

            return new AnalysisRun(cancellationToken, progress, analysisProgress).Execute(files);
        }

        // SizeBytes can be what a file occupies, less than its length (sparse
        // and compressed files on macOS and Linux). Occupied space comes in
        // 512-byte units at least, so any other SizeBytes already is the length.
        internal static long GetLength(FileSystemEntry file)
        {
            if (file.SizeBytes % 512 != 0)
            {
                return file.SizeBytes;
            }

            try
            {
                return new FileInfo(file.FullPath).Length;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return file.SizeBytes;
            }
        }

        // Blocks shared between copies count once: each file pays only for
        // the part of its extents no earlier copy holds. Proportional because
        // compressed extents map more than they occupy.
        private static long GetOccupiedBytes(
            IReadOnlyList<PhysicalFile> files,
            out bool mayBeOverstated)
        {
            mayBeOverstated = false;
            Dictionary<ulong, List<FileExtent>> seenByVolume =
                new Dictionary<ulong, List<FileExtent>>();
            FileExtentReader extentReader =
                FileIdentities.ExtentReader;
            long total = 0;

            foreach (PhysicalFile file in files)
            {
                // Checked first: what a network share answers about blocks is
                // not where they lie on the server's disk.
                if (FileIdentities.HiddenCloneChecker?.Invoke(file.PrimaryPath) == true)
                {
                    total += file.OccupiedBytes;
                    mayBeOverstated = true;
                    continue;
                }

                if (extentReader == null ||
                    !extentReader(file.PrimaryPath, out IReadOnlyList<FileExtent> extents, out ulong volume) ||
                    extents.Count == 0)
                {
                    total += file.OccupiedBytes;
                    continue;
                }

                volume = volume != 0 ? volume : file.Identity.VolumeSerialNumber;
                seenByVolume.TryGetValue(volume, out List<FileExtent> seen);
                seen ??= new List<FileExtent>();

                List<FileExtent> own = MergeExtents(extents);
                List<FileExtent> union = MergeExtents(seen.Concat(own));
                ulong mapped = GetExtentsLength(own);
                ulong added = GetExtentsLength(union) - GetExtentsLength(seen);

                total += added == mapped
                    ? file.OccupiedBytes
                    : (long)(file.OccupiedBytes * ((double)added / mapped));
                seenByVolume[volume] = union;
            }

            return total;
        }

        private static List<FileExtent> MergeExtents(
            IEnumerable<FileExtent> extents)
        {
            List<FileExtent> merged = new List<FileExtent>();

            foreach (FileExtent extent in extents.OrderBy(extent => extent.Start))
            {
                FileExtent last = merged.Count > 0 ? merged[^1] : default;

                if (merged.Count > 0 && extent.Start <= last.Start + last.Length)
                {
                    merged[^1] = last with { Length = Math.Max(last.Length, extent.Start + extent.Length - last.Start) };
                }
                else
                {
                    merged.Add(extent);
                }
            }

            return merged;
        }

        private static ulong GetExtentsLength(
            List<FileExtent> extents)
        {
            ulong length = 0;

            foreach (FileExtent extent in extents)
            {
                length += extent.Length;
            }

            return length;
        }

        private static bool TryGetFileAttributes(
            string path,
            out FileAttributes attributes)
        {
            try
            {
                attributes =
                    File.GetAttributes(path);
                return true;
            }
            catch
            {
                attributes = default;
                return false;
            }
        }

        private enum ReadStage
        {
            FirstBlock,
            LastBlock,
            Chunks,
        }

        // Files of one length that are still equal in all that was read.
        private sealed class ContentSet
        {
            public List<PhysicalFile> Files { get; init; }
            public long Length { get; init; }
            public ReadStage Stage { get; init; }
            public int ChunkSize { get; init; } = FirstChunkSize;

            // Full hashes of files of this length known from the cache. An
            // uncached file must be read to the end to compare with them, so
            // such a set is never split along the way.
            public Dictionary<string, List<PhysicalFile>> KnownHashes { get; init; }
        }

        // One pass over the candidates: every round reads a little of each
        // file still in doubt, so differing files leave after their first
        // difference and only real duplicates are read to the end.
        private sealed class AnalysisRun
        {
            private readonly CancellationToken _cancellation;
            private readonly IProgress<RedundancyAnalysisGroup> _progress;
            private readonly IProgress<RedundancyAnalysisProgressInfo> _analysisProgress;
            private readonly List<WorkingGroup> _groups = new List<WorkingGroup>();
            private readonly List<FileSystemEntry> _reparseFiles = new List<FileSystemEntry>();
            private readonly RedundancyHashCache _hashCache = RedundancyHashCacheService.Load();
            private readonly byte[] _buffer = new byte[BufferSize];
            private readonly Stopwatch _sinceProgress = Stopwatch.StartNew();
            private readonly Stopwatch _sinceCacheSave = Stopwatch.StartNew();
            private List<ContentSet> _sets = new List<ContentSet>();
            private RedundancyAnalysisPhase _lastPhase = RedundancyAnalysisPhase.Completed;
            private bool _hashCacheChanged;
            private long _bytesRead;
            private long _bytesLeft;

            public AnalysisRun(CancellationToken cancellation, IProgress<RedundancyAnalysisGroup> progress, IProgress<RedundancyAnalysisProgressInfo> analysisProgress)
            {
                _cancellation = cancellation;
                _progress = progress;
                _analysisProgress = analysisProgress;
            }

            public IReadOnlyList<RedundancyAnalysisGroup> Execute(IReadOnlyList<FileSystemEntry> files)
            {
                Report(0, RedundancyAnalysisPhase.SizeGrouping);

                try
                {
                    FindCandidates(files);

                    while (_sets.Count > 0)
                    {
                        ReadRound();
                    }
                }
                finally
                {
                    // Also when cancelled: the hashes cost reads.
                    SaveCacheIfChanged();
                    DisposeHashes();
                }

                Report(100, RedundancyAnalysisPhase.Completed);

                if (_groups.Count == 0)
                {
                    return Array.Empty<RedundancyAnalysisGroup>();
                }

                AddReparseFiles();

                return _groups
                    .OrderByDescending(group => group.TotalSizeBytes)
                    .ThenBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(ToResult)
                    .ToList();
            }

            // In the scan's order, so the metadata is read directory by directory.
            private void FindCandidates(IReadOnlyList<FileSystemEntry> files)
            {
                List<(FileSystemEntry File, long Length)> sized = files
                    .Where(file => file != null && !file.IsDirectory && file.SizeBytes > 0 && !string.IsNullOrWhiteSpace(file.FullPath))
                    .Select(file => (file, GetLength(file)))
                    .ToList();
                Dictionary<long, int> counts = new Dictionary<long, int>();

                foreach ((_, long length) in sized)
                {
                    counts[length] = counts.GetValueOrDefault(length) + 1;
                }

                List<(FileSystemEntry File, long Length)> candidates = sized.Where(item => counts[item.Length] > 1).ToList();
                Dictionary<long, Dictionary<FileIdentity, PhysicalFile>> byLength = new Dictionary<long, Dictionary<FileIdentity, PhysicalFile>>();

                for (int index = 0; index < candidates.Count; index++)
                {
                    _cancellation.ThrowIfCancellationRequested();
                    Report(index * 10 / candidates.Count, RedundancyAnalysisPhase.FileIdentity);
                    (FileSystemEntry file, long length) = candidates[index];

                    if (!TryGetFileAttributes(file.FullPath, out FileAttributes attributes) || (attributes & FileAttributes.Offline) != 0)
                    {
                        continue;
                    }

                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        _reparseFiles.Add(file);
                        continue;
                    }

                    if (!FileIdentities.Reader(file.FullPath, out FileIdentity identity, out long usn, out bool hasUsn))
                    {
                        continue;
                    }

                    if (!byLength.TryGetValue(length, out Dictionary<FileIdentity, PhysicalFile> physicalFiles))
                    {
                        byLength[length] = physicalFiles = new Dictionary<FileIdentity, PhysicalFile>();
                    }

                    if (physicalFiles.TryGetValue(identity, out PhysicalFile physicalFile))
                    {
                        physicalFile.AddLocation(file.FullPath);
                    }
                    else
                    {
                        physicalFiles.Add(identity, new PhysicalFile(identity, file.Name, file.FullPath, file.SizeBytes, length, usn, hasUsn));
                    }
                }

                foreach ((long length, Dictionary<FileIdentity, PhysicalFile> physicalFiles) in byLength)
                {
                    if (physicalFiles.Count > 1)
                    {
                        AddSet(length, physicalFiles.Values);
                    }
                }
            }

            private void AddSet(long length, IEnumerable<PhysicalFile> files)
            {
                Dictionary<string, List<PhysicalFile>> known = new Dictionary<string, List<PhysicalFile>>(StringComparer.Ordinal);
                List<PhysicalFile> unknown = new List<PhysicalFile>();

                foreach (PhysicalFile file in files)
                {
                    if (file.HasUsn && _hashCache.TryGet(file.Identity.VolumeSerialNumber, file.Identity.FileIdLow, file.Identity.FileIdHigh, length, file.Usn, out string hash))
                    {
                        AddToHash(known, hash, file);
                    }
                    else
                    {
                        unknown.Add(file);
                    }
                }

                if (known.Count > 0)
                {
                    Report(ReadPercent(), RedundancyAnalysisPhase.FullHashCache);
                }

                if (unknown.Count == 0)
                {
                    EmitKnown(known);
                }
                else if (known.Count > 0)
                {
                    _bytesLeft += length * unknown.Count;
                    _sets.Add(new ContentSet { Files = CollapseClones(unknown), Length = length, Stage = ReadStage.Chunks, KnownHashes = known });
                }
                else
                {
                    _bytesLeft += length * unknown.Count;
                    _sets.Add(new ContentSet { Files = unknown, Length = length, Stage = ReadStage.FirstBlock });
                }
            }

            private void ReadRound()
            {
                // One sweep over each disk per round, whatever the sets are.
                List<(ContentSet Set, PhysicalFile File)> work = _sets
                    .SelectMany(set => set.Files.Select(file => (set, file)))
                    .OrderBy(item => item.file.Identity.VolumeSerialNumber)
                    .ThenBy(item => item.file.GetLocation())
                    .ToList();

                foreach ((ContentSet set, PhysicalFile file) in work)
                {
                    _cancellation.ThrowIfCancellationRequested();
                    Report(ReadPercent(), set.Stage switch
                    {
                        ReadStage.FirstBlock => RedundancyAnalysisPhase.FirstBlock,
                        ReadStage.LastBlock => RedundancyAnalysisPhase.LastBlock,
                        _ => RedundancyAnalysisPhase.FullHashLive,
                    });
                    ReadStep(set, file);
                }

                List<ContentSet> next = new List<ContentSet>();

                foreach (ContentSet set in _sets)
                {
                    Settle(set, next);
                }

                _sets = next;

                if (_sinceCacheSave.Elapsed >= CacheSaveInterval)
                {
                    SaveCacheIfChanged();
                }
            }

            private void ReadStep(ContentSet set, PhysicalFile file)
            {
                try
                {
                    file.Key = set.Stage switch
                    {
                        ReadStage.FirstBlock => HashBlock(file, 0, (int)Math.Min(SampleBlockSize, file.Length)),
                        ReadStage.LastBlock => HashBlock(file, file.Length - SampleBlockSize, SampleBlockSize),
                        _ => HashChunk(file, set.ChunkSize),
                    };
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception)
                {
                    // Gone, locked or changed: it cannot be called a duplicate.
                    file.Key = null;
                }
            }

            private string HashBlock(PhysicalFile file, long offset, int count)
            {
                using SafeFileHandle handle = Open(file, FileOptions.RandomAccess);
                ReadExactly(handle, file, offset, count);
                _bytesRead += count;
                return Convert.ToHexString(SHA256.HashData(_buffer.AsSpan(0, count)));
            }

            private string HashChunk(PhysicalFile file, int chunkSize)
            {
                using SafeFileHandle handle = Open(file, FileOptions.SequentialScan);
                file.Hash ??= IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                long end = Math.Min(file.Length, file.Offset + chunkSize);

                while (file.Offset < end)
                {
                    _cancellation.ThrowIfCancellationRequested();
                    int count = (int)Math.Min(_buffer.Length, end - file.Offset);
                    ReadExactly(handle, file, file.Offset, count);
                    file.Hash.AppendData(_buffer, 0, count);
                    file.Offset += count;
                    _bytesRead += count;
                    _bytesLeft -= count;
                }

                return Convert.ToHexString(file.Hash.GetCurrentHash());
            }

            private static SafeFileHandle Open(PhysicalFile file, FileOptions options)
            {
                SafeFileHandle handle = File.OpenHandle(file.PrimaryPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, options);

                if (RandomAccess.GetLength(handle) != file.Length)
                {
                    handle.Dispose();
                    throw new IOException("The file changed");
                }

                return handle;
            }

            private void ReadExactly(SafeFileHandle handle, PhysicalFile file, long offset, int count)
            {
                ReadObserver?.Invoke(file.PrimaryPath, offset, count);
                int total = 0;

                while (total < count)
                {
                    int read = RandomAccess.Read(handle, _buffer.AsSpan(total, count - total), offset + total);

                    if (read == 0)
                    {
                        throw new IOException("The file changed");
                    }

                    total += read;
                }
            }

            private void Settle(ContentSet set, List<ContentSet> next)
            {
                foreach (PhysicalFile file in set.Files.Where(file => file.Key == null))
                {
                    Drop(file);
                }

                List<PhysicalFile> readable = set.Files.Where(file => file.Key != null).ToList();

                if (set.KnownHashes != null)
                {
                    if (readable.Count == 0 || readable[0].Offset == set.Length)
                    {
                        foreach (PhysicalFile file in readable)
                        {
                            foreach (PhysicalFile copy in file.WithClones())
                            {
                                RememberHash(copy, file.Key);
                                AddToHash(set.KnownHashes, file.Key, copy);
                            }
                        }

                        EmitKnown(set.KnownHashes);
                    }
                    else
                    {
                        next.Add(new ContentSet { Files = readable, Length = set.Length, Stage = ReadStage.Chunks, ChunkSize = set.ChunkSize * ChunkGrowth, KnownHashes = set.KnownHashes });
                    }

                    return;
                }

                foreach (IGrouping<string, PhysicalFile> equal in readable.GroupBy(file => file.Key, StringComparer.Ordinal))
                {
                    List<PhysicalFile> files = equal.ToList();

                    if (files.Count < 2 && files[0].Clones.Count > 0)
                    {
                        // Alone among the rest, but its clones are its copies.
                        Finish(files, files[0].Offset == set.Length ? equal.Key : null);
                    }
                    else if (files.Count < 2)
                    {
                        // The block of a small file already is its full hash.
                        if (set.Stage == ReadStage.FirstBlock && set.Length <= SampleBlockSize)
                        {
                            RememberHash(files[0], equal.Key);
                        }

                        Drop(files[0]);
                    }
                    else if (set.Stage == ReadStage.FirstBlock && set.Length <= SampleBlockSize)
                    {
                        // The block was the whole file: its hash is the file's.
                        Finish(files, equal.Key);
                    }
                    else if (set.Stage == ReadStage.FirstBlock && set.Length > 2L * SampleBlockSize)
                    {
                        next.Add(new ContentSet { Files = files, Length = set.Length, Stage = ReadStage.LastBlock });
                    }
                    else if (set.Stage != ReadStage.Chunks)
                    {
                        // About to read them whole: clones need just one read.
                        List<PhysicalFile> distinct = CollapseClones(files);

                        if (distinct.Count == 1)
                        {
                            Finish(distinct, null);
                        }
                        else
                        {
                            next.Add(new ContentSet { Files = distinct, Length = set.Length, Stage = ReadStage.Chunks });
                        }
                    }
                    else if (files[0].Offset == set.Length)
                    {
                        Finish(files, equal.Key);
                    }
                    else
                    {
                        next.Add(new ContentSet { Files = files, Length = set.Length, Stage = ReadStage.Chunks, ChunkSize = set.ChunkSize * ChunkGrowth });
                    }
                }
            }

            // Files with the same blocks in the same places hold the same data.
            private List<PhysicalFile> CollapseClones(List<PhysicalFile> files)
            {
                FileExtentReader reader = FileIdentities.ExtentReader;

                if (reader == null)
                {
                    return files;
                }

                Dictionary<string, PhysicalFile> byBlocks = new Dictionary<string, PhysicalFile>(StringComparer.Ordinal);
                List<PhysicalFile> distinct = new List<PhysicalFile>();

                foreach (PhysicalFile file in files)
                {
                    string blocks = GetBlocksKey(file, reader);

                    if (blocks != null && byBlocks.TryGetValue(blocks, out PhysicalFile clone))
                    {
                        clone.Clones.Add(file);
                        _bytesLeft -= file.Length - file.Offset;
                        file.DisposeHash();
                        continue;
                    }

                    if (blocks != null)
                    {
                        byBlocks[blocks] = file;
                    }

                    distinct.Add(file);
                }

                return distinct;
            }

            private static string GetBlocksKey(PhysicalFile file, FileExtentReader reader)
            {
                try
                {
                    if (FileIdentities.HiddenCloneChecker?.Invoke(file.PrimaryPath) == true ||
                        !reader(file.PrimaryPath, out IReadOnlyList<FileExtent> extents, out ulong volume) ||
                        extents.Count == 0 ||
                        extents.Any(extent => !extent.Exact) ||
                        DateTime.UtcNow - File.GetLastWriteTimeUtc(file.PrimaryPath) < CloneSettleTime)
                    {
                        return null;
                    }

                    return (volume != 0 ? volume : file.Identity.VolumeSerialNumber) + ":" +
                        string.Join(";", extents.Select(extent => extent.Logical + "," + extent.Start + "," + extent.Length));
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                {
                    return null;
                }
            }

            private void Drop(PhysicalFile file)
            {
                _bytesLeft -= file.Length - file.Offset;
                file.DisposeHash();
            }

            // hash is null for clones that were never read: nothing to remember.
            private void Finish(List<PhysicalFile> files, string hash)
            {
                foreach (PhysicalFile file in files)
                {
                    _bytesLeft -= file.Length - file.Offset;
                    file.DisposeHash();

                    foreach (PhysicalFile copy in hash == null ? Array.Empty<PhysicalFile>() : file.WithClones())
                    {
                        RememberHash(copy, hash);
                    }
                }

                Emit(files.SelectMany(file => file.WithClones()).ToList());
            }

            private void RememberHash(PhysicalFile file, string hash)
            {
                file.DisposeHash();

                if (file.HasUsn)
                {
                    _hashCache.Set(file.Identity.VolumeSerialNumber, file.Identity.FileIdLow, file.Identity.FileIdHigh, file.Length, file.Usn, hash);
                    _hashCacheChanged = true;
                }
            }

            private void EmitKnown(Dictionary<string, List<PhysicalFile>> known)
            {
                foreach (List<PhysicalFile> files in known.Values.Where(files => files.Count > 1))
                {
                    Emit(files);
                }
            }

            private void Emit(List<PhysicalFile> files)
            {
                _cancellation.ThrowIfCancellationRequested();
                long total = GetOccupiedBytes(files, out bool mayBeOverstated);
                WorkingGroup group = new WorkingGroup(files[0].Name, files[0].Length, total, mayBeOverstated, files);
                _groups.Add(group);
                _progress?.Report(ToResult(group));
            }

            private static RedundancyAnalysisGroup ToResult(WorkingGroup group)
            {
                return new RedundancyAnalysisGroup
                {
                    Name = group.Name,
                    SizeBytes = group.SizeBytes,
                    PhysicalCopyCount = group.PhysicalFiles.Count,
                    TotalSizeBytes = group.TotalSizeBytes,
                    TotalMayBeOverstated = group.TotalMayBeOverstated,
                    Locations = group.GetLocations(),
                };
            }

            private void AddReparseFiles()
            {
                if (_reparseFiles.Count == 0)
                {
                    return;
                }

                Dictionary<FileIdentity, WorkingGroup> groupByIdentity = new Dictionary<FileIdentity, WorkingGroup>();

                foreach (WorkingGroup group in _groups)
                {
                    foreach (PhysicalFile file in group.PhysicalFiles)
                    {
                        groupByIdentity[file.Identity] = group;
                    }
                }

                foreach (FileSystemEntry reparseFile in _reparseFiles)
                {
                    _cancellation.ThrowIfCancellationRequested();

                    if (FileIdentities.Reader(reparseFile.FullPath, out FileIdentity identity, out _, out _) &&
                        groupByIdentity.TryGetValue(identity, out WorkingGroup group))
                    {
                        group.AddReference(reparseFile.FullPath);
                    }
                }
            }

            private void SaveCacheIfChanged()
            {
                if (_hashCacheChanged)
                {
                    Report(ReadPercent(), RedundancyAnalysisPhase.Cache);
                    RedundancyHashCacheService.Save(_hashCache);
                    CacheSaveObserver?.Invoke();
                    _hashCacheChanged = false;
                }

                _sinceCacheSave.Restart();
            }

            private void DisposeHashes()
            {
                foreach (ContentSet set in _sets)
                {
                    foreach (PhysicalFile file in set.Files)
                    {
                        file.DisposeHash();
                    }
                }
            }

            // Reading is what takes time: 10 to 100 %, by bytes.
            private int ReadPercent()
            {
                long all = _bytesRead + _bytesLeft;
                return all <= 0 ? 10 : (int)(10 + _bytesRead * 90L / all);
            }

            // A new phase always shows; otherwise a few times per second.
            private void Report(int percentage, RedundancyAnalysisPhase phase)
            {
                if (_analysisProgress == null || (phase == _lastPhase && _sinceProgress.Elapsed < ProgressInterval))
                {
                    return;
                }

                _lastPhase = phase;
                _sinceProgress.Restart();
                _analysisProgress.Report(new RedundancyAnalysisProgressInfo { Percentage = Math.Clamp(percentage, 0, 100), Phase = phase });
            }

            private static void AddToHash(Dictionary<string, List<PhysicalFile>> hashes, string hash, PhysicalFile file)
            {
                if (!hashes.TryGetValue(hash, out List<PhysicalFile> files))
                {
                    hashes[hash] = files = new List<PhysicalFile>();
                }

                files.Add(file);
            }
        }

        private sealed class PhysicalFile
        {
            private readonly HashSet<string> _locations =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

            private ulong? _location;

            public PhysicalFile(
                FileIdentity identity,
                string name,
                string primaryPath,
                long occupiedBytes,
                long length,
                long usn,
                bool hasUsn)
            {
                Identity = identity;
                Name = name;
                PrimaryPath = primaryPath;
                OccupiedBytes = occupiedBytes;
                Length = length;
                Usn = usn;
                HasUsn = hasUsn;
                _locations.Add(primaryPath);
            }

            public FileIdentity Identity { get; }
            public string Name { get; }
            public string PrimaryPath { get; }
            public long OccupiedBytes { get; }
            public long Length { get; }
            public long Usn { get; }
            public bool HasUsn { get; }
            public long Offset { get; set; }
            public IncrementalHash Hash { get; set; }
            public string Key { get; set; }

            // Files with the same blocks, read through this one.
            public List<PhysicalFile> Clones { get; } = new List<PhysicalFile>();

            public IEnumerable<PhysicalFile> WithClones() => Clones.Prepend(this);

            // Read once, when first needed. Unknown ones go last, in scan order.
            public ulong GetLocation()
            {
                if (_location == null)
                {
                    FileLocationReader reader = FileIdentities.LocationReader;
                    _location = reader != null && reader(PrimaryPath, out ulong location) ? location : ulong.MaxValue;
                }

                return _location.Value;
            }

            public IEnumerable<string> Locations =>
                _locations;

            public void AddLocation(
                string path)
            {
                if (!string.IsNullOrWhiteSpace(path))
                {
                    _locations.Add(path);
                }
            }

            public void DisposeHash()
            {
                Hash?.Dispose();
                Hash = null;
            }
        }

        private sealed class WorkingGroup
        {
            private readonly HashSet<string> _references =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

            public WorkingGroup(
                string name,
                long sizeBytes,
                long totalSizeBytes,
                bool totalMayBeOverstated,
                IReadOnlyList<PhysicalFile> physicalFiles)
            {
                Name = name;
                SizeBytes = sizeBytes;
                TotalSizeBytes = totalSizeBytes;
                TotalMayBeOverstated = totalMayBeOverstated;
                PhysicalFiles = physicalFiles;
            }

            public string Name { get; }
            public long SizeBytes { get; }
            public long TotalSizeBytes { get; }
            public bool TotalMayBeOverstated { get; }
            public IReadOnlyList<PhysicalFile> PhysicalFiles { get; }

            public void AddReference(
                string path)
            {
                if (!string.IsNullOrWhiteSpace(path))
                {
                    _references.Add(path);
                }
            }

            public IReadOnlyList<string> GetLocations()
            {
                HashSet<string> locations =
                    new HashSet<string>(
                        StringComparer.OrdinalIgnoreCase);

                foreach (PhysicalFile physicalFile in
                    PhysicalFiles)
                {
                    foreach (string location in
                        physicalFile.Locations)
                    {
                        locations.Add(location);
                    }
                }

                foreach (string reference in _references)
                {
                    locations.Add(reference);
                }

                return locations
                    .OrderBy(
                        path => path,
                        StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }
    }
}
