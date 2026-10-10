using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;

namespace c2flux
{
    // Port of the WinForms ExportEntryController: CSV export to a file and
    // copies of the name, an indented tree or the CSV to the clipboard.
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

        public async Task CopyNameAsync(FileSystemEntry entry)
        {
            string name = string.IsNullOrWhiteSpace(entry?.Name) ? entry?.FullPath : entry.Name;

            if (!string.IsNullOrWhiteSpace(name))
            {
                await _owner.Clipboard.SetTextAsync(name);
                _setStatusText(LocalizationService.GetText("Status.ExportCopied") + name);
            }
        }

        public async Task CopyTreeTextAsync(FileSystemEntry entry)
        {
            string text = entry == null ? null : BuildTreeText(entry, _settings.ExportMaxDepth);

            if (!string.IsNullOrWhiteSpace(text))
            {
                await _owner.Clipboard.SetTextAsync(text);
                _setStatusText(LocalizationService.GetText("Status.ExportCopied") + entry.FullPath);
            }
        }

        public async Task CopyCsvAsync(FileSystemEntry entry)
        {
            string csv = entry == null ? null : _csvExportService.ExportToString(new[] { entry }, _settings);

            if (!string.IsNullOrEmpty(csv))
            {
                await _owner.Clipboard.SetTextAsync(csv);
                _setStatusText(LocalizationService.GetText("Status.ExportCopied") + entry.FullPath);
            }
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

            _csvExportService.Export(fileName, new[] { entry }, _settings);
            _setStatusText(LocalizationService.GetText("Status.ExportSaved") + fileName);
        }

        // Copy menu text: "Copy: Tree (3 lvl) -> Text" or "(Unlimited)".
        public string TreeCopyMenuText(string format)
        {
            string depth = _settings.ExportMaxDepth.HasValue ? _settings.ExportMaxDepth.Value + " lvl" : "Unlimited";
            return $"Copy: Tree ({depth}) -> {format}";
        }

        // The entry and its children as an indented tree with sizes, down to
        // maxDepth levels below it (all when null).
        internal static string BuildTreeText(FileSystemEntry root, int? maxDepth)
        {
            StringBuilder builder = new StringBuilder();
            Append(builder, root, string.Empty, true, 0, true, maxDepth);
            return builder.ToString().TrimEnd();
        }

        private static void Append(StringBuilder builder, FileSystemEntry entry, string prefix, bool isLast, int level, bool isRoot, int? maxDepth)
        {
            if (maxDepth.HasValue && level > maxDepth.Value)
            {
                return;
            }

            if (!isRoot)
            {
                builder.Append(prefix).Append(isLast ? "└─ " : "├─ ");
            }

            builder.Append(string.IsNullOrWhiteSpace(entry.Name) ? entry.FullPath : entry.Name)
                .Append(" [").Append(FormatSize(entry.SizeBytes)).Append(']')
                .Append('\n');

            string childPrefix = isRoot ? string.Empty : prefix + (isLast ? "   " : "│  ");

            for (int index = 0; index < entry.Children.Count; index++)
            {
                Append(builder, entry.Children[index], childPrefix, index == entry.Children.Count - 1, level + 1, false, maxDepth);
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
