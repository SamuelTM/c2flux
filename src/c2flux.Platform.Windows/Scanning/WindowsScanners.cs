using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace c2flux
{
    // The Windows scan chain, fastest first:
    //   MFT (C2FluxScanner or NtfsMftScanner, by the "c²flux Scan" setting)
    //   -> NtQueryDirectoryScanner -> DirectoryScanner -> ManagedScanner
    //
    // The adapters only connect the existing scanners to IFileSystemScanner;
    // their scanning code is unchanged. Names, status texts and warnings are
    // the ones the WinForms ScanExecutionController used.
    public static class WindowsScanners
    {
        public static ScannerPipeline CreatePipeline(AppSettings settings)
        {
            return new ScannerPipeline(Create(settings));
        }

        public static IReadOnlyList<IFileSystemScanner> Create(AppSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            return new IFileSystemScanner[]
            {
                new MftScanner(settings),
                new NtQueryScanner(settings),
                new Win32FindScanner(settings),
                new ManagedScanner(settings),
            };
        }

        internal static bool IsRootDrivePath(string rootPath)
        {
            string pathRoot = Path.GetPathRoot(rootPath);

            return !string.IsNullOrWhiteSpace(pathRoot) &&
                string.Equals(
                    Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    pathRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase);
        }
    }

    // Reads the NTFS master file table of a whole drive. Needs a fixed NTFS
    // drive root and administrator rights.
    public sealed class MftScanner : IFileSystemScanner
    {
        private readonly AppSettings _settings;

        public MftScanner(AppSettings settings)
        {
            _settings = settings;
        }

        // The setting can change between scans, so it is read every time.
        public string Name => _settings.C2FluxScan ? "C2FluxScanner" : "NtfsMftScanner";

        public string StatusTextKey => "Status.MftFastScanRunning";

        public string FailureAlertKey => "Alert.MftUnavailable";

        public ScannerSupport GetSupport(string rootPath)
        {
            bool isRootDrivePath = WindowsScanners.IsRootDrivePath(rootPath);
            bool isMftSupported = isRootDrivePath &&
                NtfsMftScanner.IsSupported(rootPath);

            AppAlertLog.AddVerboseInformation(
                "Scan",
                "MFT scanner selection",
                string.Join(
                    Environment.NewLine,
                    string.Format("Path: {0}", rootPath),
                    string.Format("IsRootDrivePath: {0}", isRootDrivePath),
                    string.Format("NtfsMftScanner.IsSupported: {0}", isMftSupported),
                    string.Format("C2FluxScan: {0}", _settings.C2FluxScan)));

            if (!isRootDrivePath)
            {
                return ScannerSupport.NotSupported("Not a drive root");
            }

            return isMftSupported
                ? ScannerSupport.Supported
                : ScannerSupport.NotSupported("Not a fixed NTFS drive, or not running as administrator");
        }

        public Task<FileSystemEntry> ScanAsync(
            string rootPath,
            IProgress<ScanProgress> progress,
            CancellationToken cancellationToken,
            PauseToken pauseToken)
        {
            return _settings.C2FluxScan
                ? new C2FluxScanner(_settings).ScanAsync(rootPath, progress, cancellationToken, pauseToken)
                : new NtfsMftScanner(_settings).ScanAsync(rootPath, progress, cancellationToken, pauseToken);
        }
    }

    // NtQueryDirectoryFile with parallel workers.
    public sealed class NtQueryScanner : IFileSystemScanner, IScannerDiagnostics
    {
        private readonly AppSettings _settings;
        private NtQueryDirectoryScanner _lastScanner;

        public NtQueryScanner(AppSettings settings)
        {
            _settings = settings;
        }

        public string Name => "NtQueryDirectoryScanner";

        public string StatusTextKey => "Status.NtQueryRunning";

        public string FailureAlertKey => "Alert.NtQueryUnavailable";

        public ScannerSupport GetSupport(string rootPath)
        {
            return ScannerSupport.Supported;
        }

        public Task<FileSystemEntry> ScanAsync(
            string rootPath,
            IProgress<ScanProgress> progress,
            CancellationToken cancellationToken,
            PauseToken pauseToken)
        {
            NtQueryDirectoryScanner scanner = new NtQueryDirectoryScanner(_settings);
            _lastScanner = scanner;
            return scanner.ScanAsync(rootPath, progress, cancellationToken, pauseToken);
        }

        public string GetPerformanceDetails()
        {
            int workerCount = Math.Clamp(Environment.ProcessorCount * 2, 4, 32);
            int directoryQueryBufferSize = _settings.NtQueryDirectoryBufferSize;
            long maximumDirectoryQueryBufferBytes = (long)workerCount * directoryQueryBufferSize;

            string details = string.Format(
                "DirectoryQueryBufferSizeBytes: {0:N0}{1}DirectoryQueryBufferSizeKiB: {2:N0}{1}WorkerCount: {3:N0}{1}MaximumDirectoryQueryBufferBytes: {4:N0}{1}MaximumDirectoryQueryBufferMiB: {5:N2}",
                directoryQueryBufferSize,
                Environment.NewLine,
                directoryQueryBufferSize / 1024D,
                workerCount,
                maximumDirectoryQueryBufferBytes,
                maximumDirectoryQueryBufferBytes / (1024D * 1024D));

            if (_lastScanner != null)
            {
                details +=
                    Environment.NewLine +
                    string.Format("ScannedFiles: {0:N0}", _lastScanner.ScannedFiles) +
                    Environment.NewLine +
                    string.Format("ScannedDirectories: {0:N0}", _lastScanner.ScannedDirectories);
            }

            return details;
        }
    }

    // FindFirstFileEx, single-threaded, with the per-folder scan cache.
    public sealed class Win32FindScanner : IFileSystemScanner
    {
        private readonly AppSettings _settings;

        public Win32FindScanner(AppSettings settings)
        {
            _settings = settings;
        }

        public string Name => "DirectoryScanner";

        public string StatusTextKey => "Status.NtQueryUnavailableNormal";

        public string FailureAlertKey => "Alert.NormalScanUnavailable";

        public ScannerSupport GetSupport(string rootPath)
        {
            return ScannerSupport.Supported;
        }

        public Task<FileSystemEntry> ScanAsync(
            string rootPath,
            IProgress<ScanProgress> progress,
            CancellationToken cancellationToken,
            PauseToken pauseToken)
        {
            return new DirectoryScanner(_settings).ScanAsync(rootPath, progress, cancellationToken, pauseToken);
        }
    }
}
