using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace c2flux
{
    // The native file dialogs of each OS (StorageProvider), taking the
    // WinForms filter strings ("CSV (*.csv)|*.csv|All (*.*)|*.*").
    public static class FileDialogs
    {
        public static async Task<string> SaveAsync(TopLevel owner, string title, string filter, string suggestedName)
        {
            IReadOnlyList<FilePickerFileType> types = ParseFilter(filter);
            IStorageFile file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = title,
                SuggestedFileName = suggestedName,
                FileTypeChoices = types,
                DefaultExtension = types.FirstOrDefault()?.Patterns?.FirstOrDefault()?.TrimStart('*', '.'),
                ShowOverwritePrompt = true,
            });

            return file?.TryGetLocalPath();
        }

        public static async Task<string> OpenAsync(TopLevel owner, string title, string filter)
        {
            IReadOnlyList<IStorageFile> files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter = ParseFilter(filter),
            });

            return files.Count == 0 ? null : files[0].TryGetLocalPath();
        }

        internal static IReadOnlyList<FilePickerFileType> ParseFilter(string filter)
        {
            string[] parts = (filter ?? string.Empty).Split('|');
            List<FilePickerFileType> types = new List<FilePickerFileType>();

            for (int index = 0; index + 1 < parts.Length; index += 2)
            {
                types.Add(new FilePickerFileType(parts[index])
                {
                    Patterns = parts[index + 1].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                });
            }

            return types;
        }
    }
}
