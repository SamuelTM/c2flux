using System;
using System.Threading;
using System.Threading.Tasks;

namespace c2flux
{
    // After a scan, the storage history details need the complete list of
    // files below the scanned path. The scan result is used as is unless the
    // platform offers something better: on Windows, a fast MFT snapshot with a
    // tree-less NtQuery scan as fallback.
    public interface IStorageHistorySnapshotSource
    {
        // Fast capture (progress 0-100). Returns null when the platform has
        // nothing faster than the scan that just finished; throws on failure,
        // and the caller then tries CaptureFallbackAsync.
        Task<FileSystemEntry> CaptureAsync(
            string rootPath,
            CancellationToken cancellationToken,
            IProgress<double> progress);

        // Slower capture used when CaptureAsync failed. Returns null when there
        // is no fallback.
        Task<FileSystemEntry> CaptureFallbackAsync(
            string rootPath,
            IProgress<ScanProgress> progress,
            CancellationToken cancellationToken,
            PauseToken pauseToken);
    }

    // Platforms without a faster way: the scan result is the snapshot.
    public sealed class ScanResultStorageHistorySnapshotSource : IStorageHistorySnapshotSource
    {
        public Task<FileSystemEntry> CaptureAsync(
            string rootPath,
            CancellationToken cancellationToken,
            IProgress<double> progress)
        {
            return Task.FromResult<FileSystemEntry>(null);
        }

        public Task<FileSystemEntry> CaptureFallbackAsync(
            string rootPath,
            IProgress<ScanProgress> progress,
            CancellationToken cancellationToken,
            PauseToken pauseToken)
        {
            return Task.FromResult<FileSystemEntry>(null);
        }
    }
}
