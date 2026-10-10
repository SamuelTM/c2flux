using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace c2flux
{
    // Tries the scanners of a platform in order, fastest first, and returns
    // the first result. A scanner that does not support the path is skipped;
    // one that fails logs its warning and hands over to the next. The last
    // supported scanner's failure is the scan's failure.
    //
    // Replaces the WinForms ScanExecutionController with the same behavior:
    // same status texts, same warnings, same "Performance" log entries.
    public sealed class ScannerPipeline
    {
        private readonly IReadOnlyList<IFileSystemScanner> _scanners;

        public ScannerPipeline(IEnumerable<IFileSystemScanner> scanners)
        {
            _scanners = (scanners ?? throw new ArgumentNullException(nameof(scanners))).ToList();

            if (_scanners.Count == 0)
            {
                throw new ArgumentException("At least one scanner is required.", nameof(scanners));
            }
        }

        public IReadOnlyList<IFileSystemScanner> Scanners => _scanners;

        // The scanners that will be tried for this path, in order.
        public IReadOnlyList<IFileSystemScanner> GetSupportedScanners(string rootPath)
        {
            List<IFileSystemScanner> supported = new List<IFileSystemScanner>();

            foreach (IFileSystemScanner scanner in _scanners)
            {
                ScannerSupport support = scanner.GetSupport(rootPath);

                if (support.IsSupported)
                {
                    supported.Add(scanner);
                }
                else
                {
                    AppAlertLog.AddVerboseInformation(
                        "Scan",
                        "Scanner skipped",
                        string.Join(
                            Environment.NewLine,
                            string.Format("Scanner: {0}", scanner.Name),
                            string.Format("Path: {0}", rootPath),
                            string.Format("Reason: {0}", support.Reason)));
                }
            }

            return supported;
        }

        public async Task<FileSystemEntry> ScanAsync(
            string rootPath,
            IProgress<ScanProgress> progress,
            CancellationToken cancellationToken,
            PauseToken pauseToken,
            Action<string> statusKeyChanged = null)
        {
            IReadOnlyList<IFileSystemScanner> scanners = GetSupportedScanners(rootPath);

            if (scanners.Count == 0)
            {
                throw new NotSupportedException("No scanner supports the path " + rootPath + ".");
            }

            for (int index = 0; index < scanners.Count; index++)
            {
                IFileSystemScanner scanner = scanners[index];
                bool isLast = index == scanners.Count - 1;

                statusKeyChanged?.Invoke(scanner.StatusTextKey);
                Stopwatch stopwatch = Stopwatch.StartNew();

                if (isLast)
                {
                    FileSystemEntry lastResult = await scanner.ScanAsync(
                        rootPath,
                        progress,
                        cancellationToken,
                        pauseToken);

                    LogPerformance(scanner, rootPath, stopwatch.Elapsed);
                    return lastResult;
                }

                try
                {
                    FileSystemEntry result = await scanner.ScanAsync(
                        rootPath,
                        progress,
                        cancellationToken,
                        pauseToken);

                    LogPerformance(scanner, rootPath, stopwatch.Elapsed);
                    return result;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    LogPerformance(scanner, rootPath, stopwatch.Elapsed);

                    if (scanner.FailureAlertKey != null)
                    {
                        AppAlertLog.AddWarning(
                            LocalizationService.GetText("Alert.Scan"),
                            LocalizationService.Format(
                                scanner.FailureAlertKey,
                                exception.Message));
                    }
                }
            }

            throw new InvalidOperationException("Unreachable: the last scanner returns or throws.");
        }

        private static void LogPerformance(IFileSystemScanner scanner, string rootPath, TimeSpan elapsed)
        {
            string details = string.Format(
                "Scanner: {0}{1}Path: {2}{1}ElapsedMilliseconds: {3:N0}",
                scanner.Name,
                Environment.NewLine,
                rootPath,
                elapsed.TotalMilliseconds);

            if (scanner is IScannerDiagnostics diagnostics)
            {
                string extra = diagnostics.GetPerformanceDetails();

                if (!string.IsNullOrEmpty(extra))
                {
                    details += Environment.NewLine + extra;
                }
            }

            AppAlertLog.AddVerboseInformation(
                "Performance",
                string.Format(
                    "{0}: {1:N0} ms",
                    scanner.Name,
                    elapsed.TotalMilliseconds),
                details);
        }
    }
}
