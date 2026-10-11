using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;

namespace c2flux
{
    public enum DatabasePathSelectionMode
    {
        None,
        MoveCurrentDatabase,
        UseExistingDatabase,
        CreateNewDatabase,
    }

    // Port of the WinForms DatabaseMoveForm: move the scan history database,
    // switch to an existing one or start a new one. Existing files are never
    // overwritten. The settings read SelectionMode and SelectedDatabasePath.
    public sealed class DatabaseMoveWindow : Window
    {
        private readonly string _currentDatabasePath;

        public DatabaseMoveWindow(string currentDatabasePath)
        {
            _currentDatabasePath = ScanHistoryService.NormalizeDatabasePath(currentDatabasePath);
            SelectedDatabasePath = _currentDatabasePath;

            Title = LocalizationService.GetText("DatabaseBrowse.Title");
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://c2flux.App/Assets/c2flux.png")));
            // WinForms caps the whole window at 630x350: this client area.
            Width = 614;
            Height = 311;
            CanResize = false;
            CanMinimize = false;
            CanMaximize = false;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            TextBox path = new TextBox { Text = _currentDatabasePath, IsReadOnly = true, Classes = { "ant" } };
            Button move = Button("DatabaseBrowse.MoveCurrent");
            Button useExisting = Button("DatabaseBrowse.UseExisting");
            Button createNew = Button("DatabaseBrowse.CreateNew");
            Button cancel = Button("Common.Cancel");
            move.IsEnabled = File.Exists(_currentDatabasePath);
            move.Click += async (_, _) => await MoveCurrentAsync();
            useExisting.Click += async (_, _) => await UseExistingAsync();
            createNew.Click += async (_, _) => await CreateNewAsync();
            cancel.IsCancel = true;
            cancel.Click += (_, _) => Close();

            Canvas canvas = new Canvas();
            Place(canvas, Label("DatabaseBrowse.CurrentPath", VerticalAlignment.Center), 20, 18, 580, 24);
            Place(canvas, path, 20, 44, 580, 32);
            Place(canvas, Label("DatabaseBrowse.Hint", VerticalAlignment.Top), 20, 84, 580, 42);
            Place(canvas, move, 195, 138, 230, 32);
            Place(canvas, useExisting, 195, 178, 230, 32);
            Place(canvas, createNew, 195, 218, 230, 32);
            Place(canvas, cancel, 505, 258, 95, 32);
            Content = canvas;

            // The first control of the WinForms form has the focus.
            Opened += (_, _) => path.Focus();
        }

        public string SelectedDatabasePath { get; private set; }

        public DatabasePathSelectionMode SelectionMode { get; private set; }

        private async Task MoveCurrentAsync()
        {
            string selected = await SelectNewDatabasePathAsync(LocalizationService.GetText("DatabaseBrowse.MoveSelectTitle"));

            if (selected == null || !await AppDialogs.ShowQuestionYesNoAsync(this, LocalizationService.GetText("DatabaseBrowse.MoveConfirm"), Title))
            {
                return;
            }

            Finish(selected, DatabasePathSelectionMode.MoveCurrentDatabase);
        }

        private async Task UseExistingAsync()
        {
            string file = await FileDialogs.OpenAsync(
                this,
                LocalizationService.GetText("DatabaseBrowse.UseExistingSelectTitle"),
                LocalizationService.GetText("DatabaseBrowse.Filter"),
                GetExistingDirectoryPath(_currentDatabasePath));

            if (file == null)
            {
                return;
            }

            string selected = ScanHistoryService.NormalizeDatabasePath(file);

            if (!string.Equals(_currentDatabasePath, selected, StringComparison.OrdinalIgnoreCase))
            {
                Finish(selected, DatabasePathSelectionMode.UseExistingDatabase);
            }
        }

        private async Task CreateNewAsync()
        {
            string selected = await SelectNewDatabasePathAsync(LocalizationService.GetText("DatabaseBrowse.CreateNewSelectTitle"));

            if (selected != null)
            {
                Finish(selected, DatabasePathSelectionMode.CreateNewDatabase);
            }
        }

        private void Finish(string path, DatabasePathSelectionMode mode)
        {
            SelectedDatabasePath = path;
            SelectionMode = mode;
            Close();
        }

        // A path where no file exists yet; asks again while the chosen one
        // exists.
        private async Task<string> SelectNewDatabasePathAsync(string title)
        {
            while (true)
            {
                string file = await FileDialogs.SaveAsync(
                    this,
                    title,
                    LocalizationService.GetText("DatabaseBrowse.Filter"),
                    Path.GetFileName(_currentDatabasePath),
                    GetExistingDirectoryPath(_currentDatabasePath),
                    overwritePrompt: false);

                if (file == null)
                {
                    return null;
                }

                string selected = ScanHistoryService.NormalizeDatabasePath(file);

                if (!File.Exists(selected))
                {
                    return selected;
                }

                if (!await AppDialogs.ShowWarningRetryCancelAsync(this, LocalizationService.GetText("DatabaseBrowse.TargetExists"), Title))
                {
                    return null;
                }
            }
        }

        private static string GetExistingDirectoryPath(string filePath)
        {
            try
            {
                string directoryPath = Path.GetDirectoryName(filePath);

                if (!string.IsNullOrWhiteSpace(directoryPath) && Directory.Exists(directoryPath))
                {
                    return directoryPath;
                }
            }
            catch
            {
            }

            return AppPaths.DataDirectory;
        }

        private static Border Label(string textKey, VerticalAlignment alignment)
        {
            return new Border
            {
                Child = new TextBlock { Text = LocalizationService.GetText(textKey), TextWrapping = TextWrapping.Wrap, VerticalAlignment = alignment },
            };
        }

        private static Button Button(string textKey)
        {
            return new Button
            {
                Content = LocalizationService.GetText(textKey),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Classes = { "ant", "dialog" },
            };
        }

        private static void Place(Canvas canvas, Control control, double x, double y, double width, double height)
        {
            Canvas.SetLeft(control, x);
            Canvas.SetTop(control, y);
            control.Width = width;
            control.Height = height;
            canvas.Children.Add(control);
        }
    }
}
