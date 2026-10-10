using System.IO;
using Avalonia.Controls;

namespace c2flux
{
    // The "Open in Explorer" context menu of the WinForms charts and tables:
    // folders open in the file manager, files are selected in it.
    public static class RevealMenu
    {
        public static void Show(Control owner, FileSystemEntry entry)
        {
            if (string.IsNullOrWhiteSpace(entry?.FullPath))
            {
                return;
            }

            MenuItem reveal = new MenuItem { Header = LocalizationService.GetText("Context.OpenInExplorer") };
            reveal.Click += (_, _) =>
            {
                if (Directory.Exists(entry.FullPath))
                {
                    FileManager.Open(entry.FullPath);
                }
                else if (File.Exists(entry.FullPath))
                {
                    FileManager.Reveal(entry.FullPath);
                }
            };
            new ContextMenu { ItemsSource = new[] { reveal } }.Open(owner);
        }
    }
}
