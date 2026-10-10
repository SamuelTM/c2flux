using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Enumeration;
using System.Threading;
using System.Threading.Tasks;
using RawFileSystemEntry = System.IO.Enumeration.FileSystemEntry;

namespace c2flux
{
    // One entry of a directory listing, as a DirectoryReader reports it.
    public readonly struct DirectoryEntryData
    {
        public DirectoryEntryData(string name, bool isDirectory, bool isLink, long length, DateTime lastWriteTimeUtc)
        {
            Name = name;
            IsDirectory = isDirectory;
            IsLink = isLink;
            Length = length;
            LastWriteTimeUtc = lastWriteTimeUtc;
        }

        public string Name { get; }
        public bool IsDirectory { get; }
        public bool IsLink { get; }
        public long Length { get; }
        public DateTime LastWriteTimeUtc { get; }
    }

    // Lists one directory into entries. Throws UnauthorizedAccessException or
    // IOException when it cannot be read (the directory is reported as
    // skipped). Adds nothing for a directory that must not be entered (another
    // volume, or a duplicate view of one): it stays as an empty entry.
    public delegate void DirectoryReader(string directoryPath, List<DirectoryEntryData> entries);

    // Portable scanner built only on .NET's FileSystemEnumerable, so it runs
    // on every OS. It is the last fallback of every platform's pipeline and,
    // until the native macOS and Linux scanners exist (phase 3), the only
    // scanner there.
    //
    // Directories are read in parallel by a pool of workers. Symbolic links and
    // other reparse points are listed as zero-byte entries and never followed,
    // so link loops cannot hang a scan. Directories that cannot be read are
    // kept as empty entries and reported as skipped.
    //
    // Known limits, addressed by the native scanners: no mount point or
    // virtual file system detection (scanning / on Linux enters /proc), and
    // hard links are counted once per link.
    public sealed class ManagedScanner : IFileSystemScanner
    {
        private const int ProgressIntervalMilliseconds = 200;
        private const int MaximumLiveChildren = 200;
        private const int MaximumSkippedDirectoryDetails = 500;

        private static readonly EnumerationOptions DirectoryEnumerationOptions = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = false,
            AttributesToSkip = 0,
            ReturnSpecialDirectories = false,
            MatchType = MatchType.Simple,
        };

        private readonly AppSettings _settings;
        private readonly Func<string, DirectoryReader> _createReader;

        // createReader: a DirectoryReader for one scan, given its root path;
        // native scanners plug their system calls in here and reuse the
        // parallel walk, tree building and progress of this class.
        public ManagedScanner(AppSettings settings, Func<string, DirectoryReader> createReader = null)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _createReader = createReader ?? (_ => ReadWithFileSystemEnumerable);
        }

        public string Name => "ManagedScanner";

        public string StatusTextKey => "Status.ScanRunning";

        public string FailureAlertKey => "Alert.ManagedScanUnavailable";

        public int MaxDegreeOfParallelism { get; set; } = Math.Clamp(Environment.ProcessorCount, 2, 16);

        public ScannerSupport GetSupport(string rootPath)
        {
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                return ScannerSupport.NotSupported("Empty path");
            }

            return Directory.Exists(rootPath)
                ? ScannerSupport.Supported
                : ScannerSupport.NotSupported("Directory does not exist");
        }

        public Task<FileSystemEntry> ScanAsync(
            string rootPath,
            IProgress<ScanProgress> progress,
            CancellationToken cancellationToken,
            PauseToken pauseToken)
        {
            return Task.Factory.StartNew(
                () => new Scan(this, rootPath, progress, cancellationToken, pauseToken).Run(),
                cancellationToken,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }

        // The default DirectoryReader: .NET's FileSystemEnumerable. Platform
        // readers that only decide which directories to enter call it.
        public static void ReadWithFileSystemEnumerable(string directoryPath, List<DirectoryEntryData> entries)
        {
            FileSystemEnumerable<DirectoryEntryData> enumerable = new FileSystemEnumerable<DirectoryEntryData>(
                directoryPath,
                (ref RawFileSystemEntry entry) => new DirectoryEntryData(
                    entry.FileName.ToString(),
                    entry.IsDirectory,
                    (entry.Attributes & FileAttributes.ReparsePoint) != 0,
                    entry.IsDirectory ? 0 : entry.Length,
                    entry.LastWriteTimeUtc.UtcDateTime),
                DirectoryEnumerationOptions);

            entries.AddRange(enumerable);
        }

        // State of one scan; the scanner itself stays reusable.
        private sealed class Scan
        {
            private readonly ManagedScanner _owner;
            private readonly string _rootPath;
            private readonly IProgress<ScanProgress> _progress;
            private readonly CancellationToken _cancellationToken;
            private readonly PauseToken _pauseToken;
            private readonly bool _showFilesInTree;
            private readonly DirectoryReader _reader;

            private readonly BlockingCollection<DirectoryNode> _pendingDirectories = new BlockingCollection<DirectoryNode>();
            private readonly ConcurrentBag<List<FileSystemEntry>> _fileBatches = new ConcurrentBag<List<FileSystemEntry>>();
            private readonly ConcurrentQueue<string> _skippedDirectoryDetails = new ConcurrentQueue<string>();
            private readonly object _progressLock = new object();

            private FileSystemEntry _rootEntry;
            private DirectoryNode _rootNode;
            private int _outstandingDirectories;
            private long _scannedBytes;
            private int _scannedDirectories;
            private int _scannedFiles;
            private int _skippedDirectories;
            private long _lastProgressTicks;
            private Exception _workerFailure;

            public Scan(
                ManagedScanner owner,
                string rootPath,
                IProgress<ScanProgress> progress,
                CancellationToken cancellationToken,
                PauseToken pauseToken)
            {
                _owner = owner;
                _rootPath = rootPath;
                _progress = progress;
                _cancellationToken = cancellationToken;
                _pauseToken = pauseToken;
                _showFilesInTree = owner._settings.ShowFilesInTree;
                _reader = owner._createReader(Path.GetFullPath(rootPath));
            }

            public FileSystemEntry Run()
            {
                string fullRootPath = Path.GetFullPath(_rootPath);
                DirectoryInfo rootInfo = new DirectoryInfo(fullRootPath);

                _rootEntry = new FileSystemEntry
                {
                    Name = string.IsNullOrEmpty(rootInfo.Name) ? fullRootPath : rootInfo.Name,
                    FullPath = fullRootPath,
                    IsDirectory = true,
                    LastWriteTimeUtc = rootInfo.Exists ? rootInfo.LastWriteTimeUtc : DateTime.MinValue,
                };

                _scannedDirectories = 1;
                _outstandingDirectories = 1;
                _rootNode = new DirectoryNode(_rootEntry, null);
                _pendingDirectories.Add(_rootNode);
                ReportProgress(fullRootPath, force: true);

                Task[] workers = new Task[_owner.MaxDegreeOfParallelism];

                using (_cancellationToken.Register(() => _pendingDirectories.CompleteAdding()))
                {
                    for (int index = 0; index < workers.Length; index++)
                    {
                        workers[index] = Task.Run(WorkerLoop);
                    }

                    Task.WaitAll(workers);
                }

                _cancellationToken.ThrowIfCancellationRequested();

                if (_workerFailure != null)
                {
                    throw new InvalidOperationException("The scan failed: " + _workerFailure.Message, _workerFailure);
                }

                foreach (List<FileSystemEntry> batch in _fileBatches)
                {
                    _rootEntry.AllFiles.AddRange(batch);
                }

                ApplyDirectorySizes();
                ScanTree.SortChildrenBySizeDescending(_rootEntry);
                ReportProgress(fullRootPath, force: true);

                return _rootEntry;
            }

            private void WorkerLoop()
            {
                List<FileSystemEntry> files = new List<FileSystemEntry>();
                List<DirectoryEntryData> listing = new List<DirectoryEntryData>();
                _fileBatches.Add(files);

                try
                {
                    foreach (DirectoryNode directory in _pendingDirectories.GetConsumingEnumerable())
                    {
                        _pauseToken.WaitWhilePaused(_cancellationToken);

                        if (_cancellationToken.IsCancellationRequested)
                        {
                            break;
                        }

                        try
                        {
                            ReadDirectory(directory, files, listing);
                        }
                        finally
                        {
                            if (Interlocked.Decrement(ref _outstandingDirectories) == 0)
                            {
                                _pendingDirectories.CompleteAdding();
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Cancellation is reported by Run.
                }
                catch (Exception exception)
                {
                    Interlocked.CompareExchange(ref _workerFailure, exception, null);
                    _pendingDirectories.CompleteAdding();
                }
            }

            private void ReadDirectory(DirectoryNode node, List<FileSystemEntry> files, List<DirectoryEntryData> listing)
            {
                FileSystemEntry directory = node.Entry;
                List<DirectoryNode> subdirectories = new List<DirectoryNode>();
                long directFileBytes = 0;

                try
                {
                    listing.Clear();
                    _reader(directory.FullPath, listing);

                    foreach (DirectoryEntryData raw in listing)
                    {
                        string fullPath = Path.Join(directory.FullPath, raw.Name);
                        bool isLink = raw.IsLink;

                        if (raw.IsDirectory && !isLink)
                        {
                            FileSystemEntry child = new FileSystemEntry
                            {
                                Name = raw.Name,
                                FullPath = fullPath,
                                ParentEntry = directory,
                                IsDirectory = true,
                                LastWriteTimeUtc = raw.LastWriteTimeUtc,
                            };

                            subdirectories.Add(new DirectoryNode(child, node));
                            continue;
                        }

                        FileSystemEntry file = new FileSystemEntry
                        {
                            Name = raw.Name,
                            FullPath = fullPath,
                            ParentEntry = directory,
                            IsDirectory = false,
                            SizeBytes = isLink ? 0 : raw.Length,
                            LastWriteTimeUtc = raw.LastWriteTimeUtc,
                        };

                        files.Add(file);

                        if (_showFilesInTree)
                        {
                            directory.Children.Add(file);
                        }

                        directFileBytes += file.SizeBytes;
                        Interlocked.Increment(ref _scannedFiles);
                        Interlocked.Add(ref _scannedBytes, file.SizeBytes);
                    }
                }
                catch (Exception exception) when (
                    exception is UnauthorizedAccessException ||
                    exception is IOException ||
                    exception is System.Security.SecurityException)
                {
                    Interlocked.Increment(ref _skippedDirectories);

                    if (_skippedDirectoryDetails.Count < MaximumSkippedDirectoryDetails)
                    {
                        _skippedDirectoryDetails.Enqueue(directory.FullPath + " - " + exception.Message);
                    }
                }

                // Children are attached only by the worker that read this
                // directory, so no lock is needed on directory.Children.
                node.Subdirectories = subdirectories;

                foreach (DirectoryNode subdirectory in subdirectories)
                {
                    directory.Children.Add(subdirectory.Entry);
                }

                // Live sizes for the progress snapshot; the final sizes are
                // summed from the finished tree.
                for (DirectoryNode ancestor = node; ancestor != null; ancestor = ancestor.Parent)
                {
                    Interlocked.Add(ref ancestor.LiveBytes, directFileBytes);
                }

                foreach (DirectoryNode subdirectory in subdirectories)
                {
                    Interlocked.Increment(ref _scannedDirectories);
                    Interlocked.Increment(ref _outstandingDirectories);
                    _pendingDirectories.Add(subdirectory);
                }

                ReportProgress(directory.FullPath, force: false);
            }

            // Every directory's LiveBytes is, once all workers are done, the
            // total of the files below it (with or without files shown in the
            // tree, since it does not depend on Children).
            private void ApplyDirectorySizes()
            {
                Stack<DirectoryNode> pending = new Stack<DirectoryNode>();
                pending.Push(_rootNode);

                while (pending.Count > 0)
                {
                    DirectoryNode node = pending.Pop();
                    node.Entry.SizeBytes = node.LiveBytes;

                    if (node.Subdirectories != null)
                    {
                        foreach (DirectoryNode subdirectory in node.Subdirectories)
                        {
                            pending.Push(subdirectory);
                        }
                    }
                }
            }

            // The root and its direct subdirectories with the bytes found so
            // far. Only one level: deeper levels are still being filled in by
            // other workers, and the root's children never change once listed.
            private FileSystemEntry CreateLiveSnapshot()
            {
                FileSystemEntry snapshot = new FileSystemEntry
                {
                    Name = _rootEntry.Name,
                    FullPath = _rootEntry.FullPath,
                    SizeBytes = Interlocked.Read(ref _rootNode.LiveBytes),
                    IsDirectory = true,
                };

                List<DirectoryNode> subdirectories = _rootNode.Subdirectories;

                if (subdirectories == null)
                {
                    return snapshot;
                }

                List<FileSystemEntry> children = new List<FileSystemEntry>(subdirectories.Count);

                foreach (DirectoryNode subdirectory in subdirectories)
                {
                    children.Add(new FileSystemEntry
                    {
                        Name = subdirectory.Entry.Name,
                        FullPath = subdirectory.Entry.FullPath,
                        SizeBytes = Interlocked.Read(ref subdirectory.LiveBytes),
                        IsDirectory = true,
                    });
                }

                children.Sort((left, right) =>
                {
                    int bySize = right.SizeBytes.CompareTo(left.SizeBytes);
                    return bySize != 0 ? bySize : string.Compare(left.Name, right.Name, StringComparison.Ordinal);
                });

                snapshot.Children.AddRange(children.Count > MaximumLiveChildren
                    ? children.GetRange(0, MaximumLiveChildren)
                    : children);

                return snapshot;
            }

            private void ReportProgress(string currentPath, bool force)
            {
                if (_progress == null)
                {
                    return;
                }

                long now = Environment.TickCount64;

                if (!force && now - Interlocked.Read(ref _lastProgressTicks) < ProgressIntervalMilliseconds)
                {
                    return;
                }

                lock (_progressLock)
                {
                    if (!force && now - _lastProgressTicks < ProgressIntervalMilliseconds)
                    {
                        return;
                    }

                    _lastProgressTicks = now;

                    _progress.Report(new ScanProgress
                    {
                        CurrentPath = currentPath,
                        ScannedBytes = Interlocked.Read(ref _scannedBytes),
                        ScannedDirectories = Volatile.Read(ref _scannedDirectories),
                        ScannedFiles = Volatile.Read(ref _scannedFiles),
                        SkippedDirectories = Volatile.Read(ref _skippedDirectories),
                        SkippedDirectoryDetails = new List<string>(_skippedDirectoryDetails),
                        LiveRootEntry = CreateLiveSnapshot(),
                    });
                }
            }
        }

        private sealed class DirectoryNode
        {
            public DirectoryNode(FileSystemEntry entry, DirectoryNode parent)
            {
                Entry = entry;
                Parent = parent;
            }

            public FileSystemEntry Entry { get; }
            public DirectoryNode Parent { get; }

            // Set once, by the worker that read this directory.
            public volatile List<DirectoryNode> Subdirectories;

            // Bytes of the files found so far below this directory.
            public long LiveBytes;
        }
    }
}
