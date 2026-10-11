using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace c2flux
{
    public sealed class CsvExportService
    {
        public string FileFilter
        {
            get { return LocalizationService.GetText("Csv.FileFilter"); }
        }

        // rowWritten, when given, is called after each row; a canceled token
        // stops the export (OperationCanceledException).
        public void Export(string filePath, IEnumerable<FileSystemEntry> entries, AppSettings settings, Action rowWritten = null, CancellationToken cancellationToken = default)
        {
            using StreamWriter writer = new StreamWriter(filePath, false, Encoding.UTF8);
            WriteExport(writer, entries, settings, rowWritten, cancellationToken);
        }

        public string ExportToString(IEnumerable<FileSystemEntry> entries, AppSettings settings, Action rowWritten = null, CancellationToken cancellationToken = default)
        {
            using StringWriter writer = new StringWriter(CultureInfo.InvariantCulture);
            WriteExport(writer, entries, settings, rowWritten, cancellationToken);
            return writer.ToString();
        }

        // The rows an export of entries writes (the tree copy has as many):
        // each entry and what is below it, down to maxDepth levels.
        public static int CountRows(IEnumerable<FileSystemEntry> entries, int? maxDepth)
        {
            int rows = 0;
            Stack<(FileSystemEntry Entry, int Level)> pending = new Stack<(FileSystemEntry Entry, int Level)>();

            foreach (FileSystemEntry entry in entries)
            {
                pending.Push((entry, 0));
            }

            while (pending.Count > 0)
            {
                (FileSystemEntry entry, int level) = pending.Pop();

                if (maxDepth.HasValue && level > maxDepth.Value)
                {
                    continue;
                }

                rows++;

                foreach (FileSystemEntry child in entry.Children)
                {
                    pending.Push((child, level + 1));
                }
            }

            return rows;
        }

        private void WriteExport(TextWriter writer, IEnumerable<FileSystemEntry> entries, AppSettings settings, Action rowWritten, CancellationToken cancellationToken)
        {
            WriteHeader(writer, settings);

            foreach (FileSystemEntry entry in entries)
            {
                WriteEntry(writer, entry, settings, 0, rowWritten, cancellationToken);
            }
        }

        private void WriteHeader(TextWriter writer, AppSettings settings)
        {
            List<string> columns = new List<string>();

            if (settings.ExportPath)
            {
                columns.Add(LocalizationService.GetText("Csv.Path"));
            }

            columns.Add(LocalizationService.GetText("Csv.Level"));

            if (settings.ExportSizeGb)
            {
                columns.Add(LocalizationService.GetText("Csv.SizeGb"));
            }

            if (settings.ExportSizeMb)
            {
                columns.Add(LocalizationService.GetText("Csv.SizeMb"));
            }

            writer.WriteLine(string.Join(";", columns));
        }

        private void WriteEntry(TextWriter writer, FileSystemEntry entry, AppSettings settings, int level, Action rowWritten, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (settings.ExportMaxDepth.HasValue && level > settings.ExportMaxDepth.Value)
                return;

            List<string> values = new List<string>();

            if (settings.ExportPath)
            {
                values.Add("\"" + Escape(entry.FullPath) + "\"");
            }

            values.Add(level == 0 ? LocalizationService.GetText("Csv.Root") : level.ToString(CultureInfo.InvariantCulture));

            if (settings.ExportSizeGb)
            {
                values.Add(FormatDecimal(entry.SizeBytes / 1024D / 1024D / 1024D));
            }

            if (settings.ExportSizeMb)
            {
                values.Add(FormatDecimal(entry.SizeBytes / 1024D / 1024D));
            }

            writer.WriteLine(string.Join(";", values));
            rowWritten?.Invoke();

            foreach (FileSystemEntry child in entry.Children)
            {
                WriteEntry(writer, child, settings, level + 1, rowWritten, cancellationToken);
            }
        }

        private string FormatDecimal(double value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private string Escape(string value)
        {
            return (value ?? string.Empty).Replace("\"", "\"\"");
        }
    }
}
