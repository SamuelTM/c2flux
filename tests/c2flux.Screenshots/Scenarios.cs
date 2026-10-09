using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace c2flux.Screenshots
{
    // The list of captures. Names become file names, so keep them stable:
    // phase 5 compares the new UI against files with the same names.
    internal sealed class Scenarios
    {
        private static readonly TimeSpan ScanTimeout = TimeSpan.FromMinutes(3);
        private static readonly TimeSpan SettleDelay = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan WindowTimeout = TimeSpan.FromSeconds(15);

        private static readonly (string Name, string Handler)[] ViewModes =
        {
            ("table", "toolStripButtonTable_Click"),
            ("pie", "toolStripButtonPieChart_Click"),
            ("bar", "toolStripButtonBarChart_Click"),
            ("sunburst", "toolStripButtonSunburst_Click"),
            ("treemap", "toolStripButtonTreemap_Click"),
        };

        private static readonly (string Name, string Handler)[] SettingsTabs =
        {
            ("settings", "buttonGeneralTab_Click"),
            ("settings-export", "buttonExportTab_Click"),
            ("settings-colors", "buttonColorsTab_Click"),
            ("settings-layout", "buttonLayoutTab_Click"),
            ("settings-statistics", "buttonStatisticsTab_Click"),
            ("settings-logging", "buttonLoggingTab_Click"),
        };

        private readonly AppHost _app;
        private readonly Options _options;
        private readonly CaptureLog _log;

        public Scenarios(AppHost app, Options options, CaptureLog log)
        {
            _app = app;
            _options = options;
            _log = log;
        }

        public async Task RunAllAsync()
        {
            await RunMainWindowEmptyAsync();
            await RunMainWindowScannedAsync();

            await RunSettingsAsync();
            await RunStandaloneAsync("about", () => _app.CreateForm("AboutForm", _app.LoadSettings()));

            await RunStandaloneAsync("alert-history", () => _app.CreateForm("AlertHistoryForm", _app.LoadSettings()));
            AddSampleAlerts();
            await RunStandaloneAsync("alert-history-entries", () => _app.CreateForm("AlertHistoryForm", _app.LoadSettings()));

            await RunScanHistoryAsync();
            await RunStorageHistoryAsync();

            await RunStandaloneAsync("update-available", () =>
                _app.CreateForm("UpdateAvailableForm", AppHost.Get(_app.LoadSettings(), "Layout"), CreateSampleUpdateResult()));

            await RunDialogAsync("dialog-warning-ok", () => _app.CallStatic(
                "AppDialogs", "ShowWarningOk",
                _app.LoadSettings(), "The selected folder could not be read completely. Some sizes may be too small.", "c² flux", "OK"));
            await RunDialogAsync("dialog-warning-yes-no", () => _app.CallStatic(
                "AppDialogs", "ShowWarningYesNo",
                _app.LoadSettings(), "Do you really want to delete the scan history of this drive?", "c² flux", "Yes", "No"));
            await RunDialogAsync("dialog-elevation-prompt", () => _app.CallStatic(
                "AppDialogs", "ShowElevationPrompt", _app.LoadSettings()));

            await RunStandaloneAsync("database-move", () =>
            {
                object settings = _app.LoadSettings();
                return _app.CreateForm(
                    "DatabaseMoveForm",
                    AppHost.Get(settings, "Layout"),
                    (string)AppHost.Get(settings, "ScanHistoryDatabasePath"));
            });
            await RunStandaloneAsync("debug-class", () =>
                _app.CreateForm("DebugClassForm", AppHost.Get(_app.LoadSettings(), "Layout")));
        }

        // ----- main window ------------------------------------------------

        private async Task RunMainWindowEmptyAsync()
        {
            if (!_options.ShouldRun("main-empty"))
            {
                return;
            }

            Form main = null;

            try
            {
                main = _app.CreateForm("MainForm");
                main.Show();
                await SettleAsync();
                Save("main-empty", main);
            }
            catch (Exception exception)
            {
                Fail("main-empty", exception);
            }
            finally
            {
                await CloseAsync(main);
            }
        }

        private async Task RunMainWindowScannedAsync()
        {
            if (string.IsNullOrEmpty(_options.ScanPath))
            {
                _log.Info("Skipping scanned main window captures: no --scan path.");
                return;
            }

            Form main = null;

            try
            {
                main = _app.CreateForm("MainForm", _options.ScanPath);
                main.Show();
                await WaitForScanAsync(main, null);
                await SettleAsync();

                foreach ((string name, string handler) in ViewModes)
                {
                    await RunStepAsync("main-" + name, main, () => AppHost.Invoke(main, handler, null, EventArgs.Empty));
                }

                await RunStepAsync("main-analysis", main, () => AppHost.Invoke(main, "menuItemAdvancedFeatures_Click", null, EventArgs.Empty));
                await CaptureTabsAsync("main-analysis", main, FindChild(main, "AdvancedFeaturesForm"), main);

                await RunStepAsync("main-storage-history", main, () => AppHost.Invoke(main, "menuItemStorageHistory_Click", null, EventArgs.Empty));

                AppHost.Invoke(main, "toolStripButtonTable_Click", null, EventArgs.Empty);
                await SettleAsync();

                await CaptureMenusAsync(main);
                await CaptureContextMenuAsync("context-menu-tree", main, "contextMenuStripTreeEntries", "treeViewEntries");
                await CaptureContextMenuAsync("context-menu-toolbar", main, "contextMenuStripToolbars", "toolStripMain");

                await RunSearchAsync(main);

                // A second scan after changing the volume gives the scan and
                // storage histories something to show and compare.
                if (ChangeScannedVolume())
                {
                    object firstRoot = AppHost.Get(main, "_currentRootEntry");
                    AppHost.Invoke(main, "toolStripButtonScan_Click", null, EventArgs.Empty);
                    await WaitForScanAsync(main, firstRoot);
                    await SettleAsync();
                }
            }
            catch (Exception exception)
            {
                Fail("main-scanned", exception);
            }
            finally
            {
                await CloseAsync(main);
            }
        }

        private async Task WaitForScanAsync(Form main, object previousRoot)
        {
            Stopwatch clock = Stopwatch.StartNew();
            bool scanned = await WaitUntilAsync(
                () =>
                {
                    object root = AppHost.Get(main, "_currentRootEntry");
                    return root != null && !ReferenceEquals(root, previousRoot);
                },
                ScanTimeout);

            _log.Info(string.Format("Scan of {0}: {1} after {2:0.0}s", _options.ScanPath, scanned ? "done" : "timed out", clock.Elapsed.TotalSeconds));

            if (!scanned)
            {
                throw new TimeoutException("Scan did not finish within " + ScanTimeout + ".");
            }
        }

        private bool ChangeScannedVolume()
        {
            try
            {
                string tree = Path.Combine(_options.ScanPath, "tree");

                File.WriteAllBytes(Path.Combine(tree, "added-after-first-scan.bin"), new byte[8 * 1024 * 1024]);
                File.Delete(Path.Combine(tree, "wide", "file-000000.dat"));
                File.AppendAllText(Path.Combine(tree, "symlinks", "target.txt"), new string('x', 4096));

                _log.Info("Changed the scanned volume: 1 file added, 1 deleted, 1 grown.");
                return true;
            }
            catch (Exception exception)
            {
                _log.Info("Could not change the scanned volume: " + exception.Message);
                return false;
            }
        }

        private async Task CaptureMenusAsync(Form main)
        {
            MenuStrip menu = (MenuStrip)AppHost.Get(main, "menuStripMain");

            foreach (ToolStripMenuItem item in menu.Items.OfType<ToolStripMenuItem>())
            {
                string name = "menu-" + Slug(item.Text);

                if (!_options.ShouldRun(name))
                {
                    continue;
                }

                try
                {
                    item.ShowDropDown();
                    await Task.Delay(500);
                    SaveScreen(name, Rectangle.Union(main.Bounds, item.DropDown.Bounds), "ToolStripDropDown", item.Text);
                }
                catch (Exception exception)
                {
                    Fail(name, exception);
                }
                finally
                {
                    item.HideDropDown();
                    await Task.Delay(300);
                }
            }
        }

        private async Task CaptureContextMenuAsync(string name, Form main, string menuField, string targetField)
        {
            if (!_options.ShouldRun(name))
            {
                return;
            }

            ContextMenuStrip menu = null;

            try
            {
                menu = (ContextMenuStrip)AppHost.Get(main, menuField);
                Control target = (Control)AppHost.Get(main, targetField);
                menu.Show(target, new Point(60, 30));
                await Task.Delay(500);
                SaveScreen(name, Rectangle.Union(main.Bounds, menu.Bounds), "ContextMenuStrip", null);
            }
            catch (Exception exception)
            {
                Fail(name, exception);
            }
            finally
            {
                menu?.Close();
                await Task.Delay(300);
            }
        }

        private async Task RunSearchAsync(Form main)
        {
            if (!_options.ShouldRun("search"))
            {
                return;
            }

            Form search = null;

            try
            {
                HashSet<Form> before = OpenForms();
                AppHost.Invoke(main, "toolStripButtonSearch_Click", null, EventArgs.Empty);
                search = await WaitForNewFormAsync(before);
                await SettleAsync();
                Save("search", search);

                Control input = (Control)AppHost.Get(search, "textBoxSearch");
                input.Text = "file-00";
                AppHost.Invoke(search, "buttonSearch_Click", null, EventArgs.Empty);
                await Task.Delay(TimeSpan.FromSeconds(4));
                Save("search-results", search);
            }
            catch (Exception exception)
            {
                Fail("search", exception);
            }
            finally
            {
                await CloseAsync(search);
            }
        }

        // ----- other windows -----------------------------------------------

        private async Task RunSettingsAsync()
        {
            if (!SettingsTabs.Any(tab => _options.ShouldRun(tab.Name)))
            {
                return;
            }

            Form settings = null;

            try
            {
                settings = ShowAt(_app.CreateForm("SettingsForm", _app.LoadSettings()));
                await SettleAsync();

                foreach ((string name, string handler) in SettingsTabs)
                {
                    await RunStepAsync(name, settings, () => AppHost.Invoke(settings, handler, null, EventArgs.Empty));
                }
            }
            catch (Exception exception)
            {
                Fail("settings", exception);
            }
            finally
            {
                await CloseAsync(settings);
            }
        }

        private async Task RunScanHistoryAsync()
        {
            if (!_options.ShouldRun("scan-history"))
            {
                return;
            }

            Form history = null;

            try
            {
                history = ShowAt(_app.CreateForm("ScanHistoryForm", _app.LoadSettings()));
                await SettleAsync();
                Save("scan-history", history);

                AppHost.Invoke(history, "buttonCompare_Click", null, EventArgs.Empty);
                await Task.Delay(TimeSpan.FromSeconds(4));
                await CaptureTabsAsync("scan-history", history, FindChildOfType(history, "Tabs"), history);
            }
            catch (Exception exception)
            {
                Fail("scan-history", exception);
            }

            await CapturePopupsAsync("scan-history", history);
            await CloseAsync(history);
        }

        private async Task RunStorageHistoryAsync()
        {
            if (!_options.ShouldRun("storage-history"))
            {
                return;
            }

            Form history = null;

            try
            {
                history = ShowAt(_app.CreateForm("StorageHistoryForm", _app.LoadSettings()));
                await SettleAsync();
                Save("storage-history", history);

                object record = FindRecord(history);

                if (record == null)
                {
                    Fail("storage-history-details", new InvalidOperationException("No storage history record to open."));
                }
                else
                {
                    Form owner = history;
                    await RunDialogAsync("storage-history-details", () =>
                    {
                        SetField(owner, "_contextMenuRecord", record);
                        AppHost.Invoke(owner, "contextMenuItemDetails_Click", null, EventArgs.Empty);
                    });
                }
            }
            catch (Exception exception)
            {
                Fail("storage-history", exception);
            }

            await CloseAsync(history);
        }

        private object FindRecord(Form history)
        {
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

            foreach (FieldInfo field in history.GetType().GetFields(flags))
            {
                if (field.GetValue(history) is IEnumerable values && !(values is string))
                {
                    object first = values.Cast<object>().FirstOrDefault(value => value?.GetType().Name == "StorageHistoryRecord");

                    if (first != null)
                    {
                        return values.Cast<object>().Last(value => value?.GetType().Name == "StorageHistoryRecord");
                    }
                }
            }

            return null;
        }

        private object CreateSampleUpdateResult()
        {
            object result = Activator.CreateInstance(_app.GetType("GitHubUpdateResult"));
            SetProperty(result, "CanConnectToGitHub", true);
            SetProperty(result, "UpdateAvailable", true);
            SetProperty(result, "LatestVersion", "9.9.9");
            SetProperty(result, "DownloadUrl", "https://example.invalid/c2flux.zip");
            SetProperty(result, "ReleaseNotes",
                "## Changelog v9.9.9\n\n- Sample release notes used for UI screenshots.\n- Second line.\n- Third line.");
            return result;
        }

        private void AddSampleAlerts()
        {
            try
            {
                _app.CallStatic("AppAlertLog", "AddInformation", "Scan", "Scan of T:\\ completed.");
                _app.CallStatic("AppAlertLog", "AddWarning", "Scan", "3 folders could not be read.", "T:\\tree\\unreadable\\locked-dir\nAccess is denied.");
                _app.CallStatic("AppAlertLog", "AddError", "Export", "The CSV file could not be written.", "The file is in use by another process.");
            }
            catch (Exception exception)
            {
                _log.Info("Could not add sample alerts: " + Program.Unwrap(exception).Message);
            }
        }

        // ----- building blocks ----------------------------------------------

        private async Task RunStepAsync(string name, Form form, Action action)
        {
            if (!_options.ShouldRun(name))
            {
                return;
            }

            try
            {
                action();
                await SettleAsync();
                Save(name, form);
            }
            catch (Exception exception)
            {
                Fail(name, exception);
            }

            await CapturePopupsAsync(name, form);
        }

        private async Task RunStandaloneAsync(string name, Func<Form> create)
        {
            if (!_options.ShouldRun(name))
            {
                return;
            }

            Form form = null;

            try
            {
                form = ShowAt(create());
                await SettleAsync();
                Save(name, form);
            }
            catch (Exception exception)
            {
                Fail(name, exception);
            }

            await CapturePopupsAsync(name, form);
            await CloseAsync(form);
        }

        // Modal dialogs block the code that opens them, so they are opened
        // from a posted callback; this method keeps running inside the
        // dialog's message loop, captures it and closes it.
        private async Task RunDialogAsync(string name, Action showModal)
        {
            if (!_options.ShouldRun(name))
            {
                return;
            }

            Form dialog = null;

            try
            {
                HashSet<Form> before = OpenForms();
                Exception thrown = null;

                SynchronizationContext.Current.Post(_ =>
                {
                    try
                    {
                        showModal();
                    }
                    catch (Exception exception)
                    {
                        thrown = exception;
                    }
                }, null);

                dialog = await WaitForNewFormAsync(before, () => thrown);
                await SettleAsync();
                Save(name, dialog);
            }
            catch (Exception exception)
            {
                Fail(name, exception);
            }
            finally
            {
                await CloseAsync(dialog);
            }
        }

        private async Task CaptureTabsAsync(string prefix, Form captureTarget, Control container, Form popupOwner)
        {
            Control tabs = container == null ? null : FindChildOfType(container, "Tabs");

            if (tabs == null)
            {
                _log.Info(prefix + ": no tabs found.");
                return;
            }

            IList pages = (IList)AppHost.Get(tabs, "Pages");

            for (int index = 1; index < pages.Count; index++)
            {
                object page = pages[index];
                string name = prefix + "-" + Slug((string)AppHost.Get(page, "Text"));
                await RunStepAsync(name, captureTarget, () => SetProperty(tabs, "SelectedTab", page));
            }

            SetProperty(tabs, "SelectedTab", pages[0]);
            await CapturePopupsAsync(prefix, popupOwner);
        }

        // Unexpected windows (message boxes, errors) are captured too: they are
        // part of what the user would see, and closing them unblocks the run.
        private async Task CapturePopupsAsync(string scenario, Form expected)
        {
            int index = 0;

            foreach (Form popup in Application.OpenForms.Cast<Form>().ToList())
            {
                // Forms embedded in another window (TopLevel = false, like the
                // main window's analysis and storage history panels) are part of
                // that window's capture, not popups.
                if (popup == expected || popup.IsDisposed || IsMainForm(popup) || !popup.Visible || !popup.TopLevel ||
                    (expected != null && popup == expected.Owner))
                {
                    continue;
                }

                index++;
                string name = scenario + "--popup-" + index;
                _log.Info(string.Format("{0}: unexpected window {1} \"{2}\"", scenario, popup.GetType().Name, popup.Text));
                Save(name, popup);
                await CloseAsync(popup);
            }
        }

        private static bool IsMainForm(Form form)
        {
            return form.GetType().Name == "MainForm";
        }

        private static Form ShowAt(Form form)
        {
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(40, 40);
            form.Show();
            return form;
        }

        private static HashSet<Form> OpenForms()
        {
            return new HashSet<Form>(Application.OpenForms.Cast<Form>());
        }

        private static async Task<Form> WaitForNewFormAsync(HashSet<Form> before, Func<Exception> failure = null)
        {
            Form found = null;

            await WaitUntilAsync(() =>
            {
                if (failure?.Invoke() != null)
                {
                    return true;
                }

                found = Application.OpenForms.Cast<Form>().FirstOrDefault(form => !before.Contains(form) && form.Visible);
                return found != null;
            }, WindowTimeout);

            Exception thrown = failure?.Invoke();

            if (thrown != null)
            {
                throw new InvalidOperationException("Opening the window failed.", Program.Unwrap(thrown));
            }

            return found ?? throw new TimeoutException("The window did not open.");
        }

        private static Control FindChild(Control root, string typeName)
        {
            return Descendants(root).FirstOrDefault(control => control.GetType().Name == typeName);
        }

        private static Control FindChildOfType(Control root, string typeName)
        {
            return FindChild(root, typeName);
        }

        private static IEnumerable<Control> Descendants(Control root)
        {
            foreach (Control child in root.Controls)
            {
                yield return child;

                foreach (Control descendant in Descendants(child))
                {
                    yield return descendant;
                }
            }
        }

        private static void SetProperty(object instance, string name, object value)
        {
            PropertyInfo property = instance.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            if (property == null)
            {
                throw new InvalidOperationException(instance.GetType().Name + "." + name + " not found.");
            }

            property.SetValue(instance, value);
        }

        private static void SetField(object instance, string name, object value)
        {
            FieldInfo field = instance.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            if (field == null)
            {
                throw new InvalidOperationException(instance.GetType().Name + "." + name + " not found.");
            }

            field.SetValue(instance, value);
        }

        private static string Slug(string text)
        {
            string slug = new string((text ?? string.Empty)
                .Replace("&", string.Empty)
                .ToLowerInvariant()
                .Select(character => char.IsLetterOrDigit(character) ? character : '-')
                .ToArray());

            while (slug.Contains("--"))
            {
                slug = slug.Replace("--", "-");
            }

            return slug.Trim('-');
        }

        private void Save(string name, Control window)
        {
            string file = name + ".png";
            CaptureRecord record = new CaptureRecord
            {
                Name = name,
                File = file,
                FormType = window.GetType().Name,
                Title = window.Text,
            };

            try
            {
                using (Bitmap bitmap = WindowCapture.Capture(window, out string method))
                {
                    bitmap.Save(Path.Combine(_options.OutputDirectory, file), ImageFormat.Png);
                    record.Method = method;
                    record.Width = bitmap.Width;
                    record.Height = bitmap.Height;
                }

                // WinForms shows this dialog for exceptions the app did not
                // handle: the capture worked, but it documents an app bug.
                record.Status = window.GetType().Name == "ThreadExceptionDialog" ? "app-error" : "ok";
                _log.Info(string.Format("{0}: {1}x{2} via {3}", name, record.Width, record.Height, record.Method));
            }
            catch (Exception exception)
            {
                record.Status = "error";
                record.Error = Program.Unwrap(exception).ToString();
                _log.Info(name + ": capture failed: " + Program.Unwrap(exception).Message);
            }

            _log.Captures.Add(record);
        }

        private void SaveScreen(string name, Rectangle area, string type, string title)
        {
            string file = name + ".png";

            using (Bitmap bitmap = WindowCapture.CaptureScreen(area))
            {
                bitmap.Save(Path.Combine(_options.OutputDirectory, file), ImageFormat.Png);
            }

            _log.Captures.Add(new CaptureRecord
            {
                Name = name,
                File = file,
                Status = "ok",
                Method = "CopyFromScreen",
                FormType = type,
                Title = title,
                Width = area.Width,
                Height = area.Height,
            });
            _log.Info(string.Format("{0}: {1}x{2} via CopyFromScreen", name, area.Width, area.Height));
        }

        private void Fail(string name, Exception exception)
        {
            Exception cause = Program.Unwrap(exception);
            _log.Captures.Add(new CaptureRecord { Name = name, Status = "error", Error = cause.ToString() });
            _log.Info(name + ": failed: " + cause.GetType().Name + ": " + cause.Message);
        }

        private static async Task SettleAsync()
        {
            await Task.Delay(SettleDelay);
        }

        private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
        {
            Stopwatch clock = Stopwatch.StartNew();

            while (clock.Elapsed < timeout)
            {
                if (condition())
                {
                    return true;
                }

                await Task.Delay(200);
            }

            return condition();
        }

        private static async Task CloseAsync(Form form)
        {
            if (form == null || form.IsDisposed)
            {
                return;
            }

            try
            {
                form.Close();
                await Task.Delay(300);

                if (!form.IsDisposed && !form.Modal)
                {
                    form.Dispose();
                }
            }
            catch (Exception)
            {
                // Closing is best effort; the next scenario must still run.
            }
        }
    }
}
