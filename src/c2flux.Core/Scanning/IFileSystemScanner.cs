using System;
using System.Threading;
using System.Threading.Tasks;

namespace c2flux
{
    // One way of turning a folder into a FileSystemEntry tree. Each platform
    // provides an ordered list of them, fastest first; ScannerPipeline tries
    // them in turn. See ROADMAP.md, phase 2.
    public interface IFileSystemScanner
    {
        // Short technical name for logs, e.g. "C2FluxScanner".
        string Name { get; }

        // LocalizationService key of the status bar text shown while this
        // scanner runs, e.g. "Status.MftFastScanRunning".
        string StatusTextKey { get; }

        // LocalizationService format key ({0} = exception message) of the
        // warning logged when this scanner fails and the next one is tried.
        // Null for scanners whose failure needs no specific warning.
        string FailureAlertKey { get; }

        // Quick and side-effect free (besides diagnostic logging): whether this
        // scanner can run on the path at all (file system, permissions, path
        // kind). A supported scanner may still fail while scanning.
        ScannerSupport GetSupport(string rootPath);

        Task<FileSystemEntry> ScanAsync(
            string rootPath,
            IProgress<ScanProgress> progress,
            CancellationToken cancellationToken,
            PauseToken pauseToken);
    }

    // Optional: extra lines for the "Performance" log entry written after a
    // scan attempt (worker counts, buffer sizes, ...).
    public interface IScannerDiagnostics
    {
        string GetPerformanceDetails();
    }

    public readonly struct ScannerSupport
    {
        private ScannerSupport(bool isSupported, string reason)
        {
            IsSupported = isSupported;
            Reason = reason;
        }

        public bool IsSupported { get; }

        // Why the scanner cannot run, for logs. Not localized.
        public string Reason { get; }

        public static ScannerSupport Supported => new ScannerSupport(true, null);

        public static ScannerSupport NotSupported(string reason)
        {
            return new ScannerSupport(false, reason);
        }
    }
}
