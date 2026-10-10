using System;
using System.Threading;
using System.Threading.Tasks;

namespace c2flux
{
    // What MainForm did directly before phase 2: an MFT snapshot through
    // C2FluxScanner, and NtQueryDirectoryScanner without building the
    // directory tree when that fails.
    public sealed class WindowsStorageHistorySnapshotSource : IStorageHistorySnapshotSource
    {
        private readonly AppSettings _settings;

        public WindowsStorageHistorySnapshotSource(AppSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public Task<FileSystemEntry> CaptureAsync(
            string rootPath,
            CancellationToken cancellationToken,
            IProgress<double> progress)
        {
            return new C2FluxScanner(_settings).CaptureStorageHistoryDetailsSnapshotAsync(
                rootPath,
                cancellationToken,
                progress);
        }

        public Task<FileSystemEntry> CaptureFallbackAsync(
            string rootPath,
            IProgress<ScanProgress> progress,
            CancellationToken cancellationToken,
            PauseToken pauseToken)
        {
            return new NtQueryDirectoryScanner(_settings).ScanAsync(
                rootPath,
                progress,
                cancellationToken,
                pauseToken,
                false);
        }
    }
}
