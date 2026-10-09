using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
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

        private static readonly (string Name, string Handler)[] ViewModes =
        {
            ("table", "toolStripButtonTable_Click"),
            ("pie", "toolStripButtonPieChart_Click"),
            ("bar", "toolStripButtonBarChart_Click"),
            ("sunburst", "toolStripButtonSunburst_Click"),
            ("treemap", "toolStripButtonTreemap_Click"),
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

            await RunStandaloneAsync("settings", () => _app.CreateForm("SettingsForm", _app.LoadSettings()));
            await RunStandaloneAsync("about", () => _app.CreateForm("AboutForm", _app.LoadSettings()));
            await RunStandaloneAsync("alert-history", () => _app.CreateForm("AlertHistoryForm", _app.LoadSettings()));
            await RunStandaloneAsync("scan-history", () => _app.CreateForm("ScanHistoryForm", _app.LoadSettings()));
            await RunStandaloneAsync("storage-history", () => _app.CreateForm("StorageHistoryForm", _app.LoadSettings()));
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
            if (!_options.ShouldRun("main-"))
            {
                return;
            }

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

                Stopwatch scanClock = Stopwatch.StartNew();
                bool scanned = await WaitUntilAsync(() => AppHost.Get(main, "_currentRootEntry") != null, ScanTimeout);
                _log.Info(string.Format("Scan of {0}: {1} after {2:0.0}s", _options.ScanPath, scanned ? "done" : "timed out", scanClock.Elapsed.TotalSeconds));

                if (!scanned)
                {
                    throw new TimeoutException("Scan did not finish within " + ScanTimeout + ".");
                }

                await SettleAsync();

                foreach ((string name, string handler) in ViewModes)
                {
                    await RunStepAsync("main-" + name, main, () => AppHost.Invoke(main, handler, null, EventArgs.Empty));
                }

                await RunStepAsync("main-analysis", main, () => AppHost.Invoke(main, "menuItemAdvancedFeatures_Click", null, EventArgs.Empty));
                await RunStepAsync("main-storage-history", main, () => AppHost.Invoke(main, "menuItemStorageHistory_Click", null, EventArgs.Empty));

                // Back to the default view before opening the search window on top.
                AppHost.Invoke(main, "toolStripButtonTable_Click", null, EventArgs.Empty);
                await SettleAsync();

                if (_options.ShouldRun("search"))
                {
                    HashSet<Form> before = new HashSet<Form>(Application.OpenForms.Cast<Form>());
                    AppHost.Invoke(main, "toolStripButtonSearch_Click", null, EventArgs.Empty);
                    await SettleAsync();

                    Form search = Application.OpenForms.Cast<Form>().FirstOrDefault(form => !before.Contains(form));

                    if (search == null)
                    {
                        Fail("search", new InvalidOperationException("The search window did not open."));
                    }
                    else
                    {
                        Save("search", search);
                        await CloseAsync(search);
                    }
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
                form = create();
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(40, 40);
                form.Show();
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
                if (popup == expected || popup.IsDisposed || IsMainForm(popup) || !popup.Visible || !popup.TopLevel)
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

        private void Save(string name, Form form)
        {
            string file = name + ".png";
            CaptureRecord record = new CaptureRecord
            {
                Name = name,
                File = file,
                FormType = form.GetType().Name,
                Title = form.Text,
            };

            try
            {
                using (Bitmap bitmap = WindowCapture.Capture(form, out string method))
                {
                    bitmap.Save(Path.Combine(_options.OutputDirectory, file), ImageFormat.Png);
                    record.Method = method;
                    record.Width = bitmap.Width;
                    record.Height = bitmap.Height;
                }

                // WinForms shows this dialog for exceptions the app did not
                // handle: the capture worked, but it documents an app bug.
                record.Status = form.GetType().Name == "ThreadExceptionDialog" ? "app-error" : "ok";
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

                if (!form.IsDisposed)
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
