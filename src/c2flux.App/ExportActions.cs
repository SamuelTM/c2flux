using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;

namespace c2flux
{
    // Port of the WinForms ExportEntryController: CSV export to a file and
    // copies of the path, an indented tree or the CSV to the clipboard. The
    // exports run off the UI thread, with a progress dialog that can cancel.
    public sealed class ExportActions
    {
        private readonly CsvExportService _csvExportService = new CsvExportService();
        private readonly AppSettings _settings;
        private readonly TopLevel _owner;
        private readonly Action<string> _setStatusText;

        public ExportActions(AppSettings settings, TopLevel owner, Action<string> setStatusText)
        {
            _settings = settings;
            _owner = owner;
            _setStatusText = setStatusText;
        }

        public async Task CopyPathAsync(FileSystemEntry entry)
        {
            string path = string.IsNullOrWhiteSpace(entry?.FullPath) ? entry?.Name : entry.FullPath;

            if (!string.IsNullOrWhiteSpace(path))
            {
                await _owner.Clipboard.SetTextAsync(path);
                _setStatusText(LocalizationService.GetText("Status.ExportCopied") + path);
            }
        }

        public async Task CopyTreeTextAsync(FileSystemEntry entry)
        {
            (bool completed, string text) = entry == null ? (false, null) : await RunWithProgressAsync(entry, (rowWritten, token) => BuildTreeText(entry, _settings.ExportMaxDepth, rowWritten, token));

            if (completed && !string.IsNullOrWhiteSpace(text))
            {
                await _owner.Clipboard.SetTextAsync(text);
                _setStatusText(LocalizationService.GetText("Status.ExportCopied") + entry.FullPath);
            }
        }

        public async Task CopyCsvAsync(FileSystemEntry entry)
        {
            (bool completed, string csv) = entry == null ? (false, null) : await RunWithProgressAsync(entry, (rowWritten, token) => _csvExportService.ExportToString(new[] { entry }, _settings, rowWritten, token));

            if (completed && !string.IsNullOrEmpty(csv))
            {
                await _owner.Clipboard.SetTextAsync(csv);
                _setStatusText(LocalizationService.GetText("Status.ExportCopied") + entry.FullPath);
            }
        }

        // The rows are counted first (fast: no text), so the dialog can show
        // how far the export is.
        private Task<(bool Completed, T Result)> RunWithProgressAsync<T>(FileSystemEntry entry, Func<Action, CancellationToken, T> export)
        {
            return AppDialogs.RunWithProgressAsync(
                _owner as Window,
                LocalizationService.Format("Export.Progress", string.IsNullOrWhiteSpace(entry.FullPath) ? entry.Name : entry.FullPath),
                (report, token) =>
                {
                    int total = Math.Max(1, CsvExportService.CountRows(new[] { entry }, _settings.ExportMaxDepth));
                    int written = 0;
                    return export(() => report(Math.Min(1, ++written / (double)total)), token);
                },
                LocalizationService.GetText("Toolbar.Export"));
        }

        public async Task ExportAsync(FileSystemEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            string fileName = await FileDialogs.SaveAsync(
                _owner,
                LocalizationService.GetText("Toolbar.Export"),
                _csvExportService.FileFilter,
                CreateExportFileName(entry));

            if (fileName == null)
            {
                return;
            }

            (bool completed, bool _) = await RunWithProgressAsync(entry, (rowWritten, token) =>
            {
                try
                {
                    _csvExportService.Export(fileName, new[] { entry }, _settings, rowWritten, token);
                    return true;
                }
                catch (OperationCanceledException)
                {
                    // Not half an export left behind.
                    File.Delete(fileName);
                    throw;
                }
            });

            if (completed)
            {
                _setStatusText(LocalizationService.GetText("Status.ExportSaved") + fileName);
            }
        }

        // Copy menu text with the export depth: "Copy tree as text (all
        // levels)", "(depth 3)". key: Context.CopyTreeText or CopyTreeCsv.
        public string TreeCopyMenuText(string key)
        {
            string depth = _settings.ExportMaxDepth.HasValue
                ? LocalizationService.Format("Context.DepthLevels", _settings.ExportMaxDepth.Value)
                : LocalizationService.GetText("Context.DepthUnlimited");
            return LocalizationService.Format(key, depth);
        }

        // The entry and its children as an indented tree with sizes, down to
        // maxDepth levels below it (all when null). rowWritten, when given,
        // is called after each line; a canceled token stops it.
        internal static string BuildTreeText(FileSystemEntry root, int? maxDepth, Action rowWritten = null, CancellationToken cancellationToken = default)
        {
            StringBuilder builder = new StringBuilder();
            Append(builder, root, string.Empty, true, 0, true, maxDepth, rowWritten, cancellationToken);
            return builder.ToString().TrimEnd();
        }

        private static void Append(StringBuilder builder, FileSystemEntry entry, string prefix, bool isLast, int level, bool isRoot, int? maxDepth, Action rowWritten, CancellationToken cancellationToken)
        {
            if (maxDepth.HasValue && level > maxDepth.Value)
            {
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (!isRoot)
            {
                builder.Append(prefix).Append(isLast ? "└─ " : "├─ ");
            }

            builder.Append(string.IsNullOrWhiteSpace(entry.Name) ? entry.FullPath : entry.Name)
                .Append(" [").Append(FormatSize(entry.SizeBytes)).Append(']')
                .Append('\n');
            rowWritten?.Invoke();

            string childPrefix = isRoot ? string.Empty : prefix + (isLast ? "   " : "│  ");

            for (int index = 0; index < entry.Children.Count; index++)
            {
                Append(builder, entry.Children[index], childPrefix, index == entry.Children.Count - 1, level + 1, false, maxDepth, rowWritten, cancellationToken);
            }
        }

        // Not SizeFormatter: the WinForms copy used "0.##" with an invariant
        // decimal point.
        private static string FormatSize(long sizeBytes)
        {
            string[] units = { "TB", "GB", "MB", "KB" };
            double[] sizes = { Math.Pow(1024, 4), Math.Pow(1024, 3), Math.Pow(1024, 2), 1024 };

            for (int index = 0; index < units.Length; index++)
            {
                if (sizeBytes >= sizes[index])
                {
                    return (sizeBytes / sizes[index]).ToString("0.##", CultureInfo.InvariantCulture) + " " + units[index];
                }
            }

            return sizeBytes.ToString(CultureInfo.InvariantCulture) + " B";
        }

        private static string CreateExportFileName(FileSystemEntry entry)
        {
            string name = string.IsNullOrWhiteSpace(entry.Name) ? "wtf-scan" : entry.Name;

            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(invalid, '_');
            }

            return name + ".csv";
        }
    }
}
