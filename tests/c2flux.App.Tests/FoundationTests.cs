using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Xunit;

namespace c2flux.AppTests
{
    public class FoundationTests
    {
        // A toolbar button enabled again (Analysis after a scan) draws its
        // icon in color right away, not only once the mouse passes over it.
        [AvaloniaFact]
        public void A_toolbar_icon_redraws_when_its_button_is_enabled_again()
        {
            byte[] IconPixels(bool startEnabled)
            {
                ToolbarIcon icon = new ToolbarIcon { Kind = ToolbarIconKind.Analysis };
                Button button = new Button { Content = icon, IsEnabled = startEnabled };
                Window window = new Window { Width = 120, Height = 60, Content = button };
                window.Show();
                Dispatcher.UIThread.RunJobs();
                Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window).Dispose();
                button.IsEnabled = true;
                Dispatcher.UIThread.RunJobs();

                using Avalonia.Media.Imaging.WriteableBitmap frame = Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window);
                using Avalonia.Platform.ILockedFramebuffer buffer = frame.Lock();
                Avalonia.Point origin = Avalonia.VisualExtensions.TranslatePoint(icon, new Avalonia.Point(0, 0), window).Value;
                int left = (int)origin.X, top = (int)origin.Y, width = (int)icon.Bounds.Width, height = (int)icon.Bounds.Height;
                byte[] pixels = new byte[width * height * 4];

                for (int row = 0; row < height; row++)
                {
                    System.Runtime.InteropServices.Marshal.Copy(buffer.Address + (top + row) * buffer.RowBytes + left * 4, pixels, row * width * 4, width * 4);
                }

                window.Close();
                return pixels;
            }

            Assert.Equal(IconPixels(startEnabled: true), IconPixels(startEnabled: false));
        }

        // The system's mode can arrive after the first frame (Linux): what is
        // drawn by hand ends up like a window that started in that mode.
        [AvaloniaFact]
        public void Hand_drawn_controls_follow_a_theme_change()
        {
            byte[] Pixels(Avalonia.Controls.Control control, bool switchAfterShown)
            {
                App.ApplyLayout(switchAfterShown ? AppLayout.WindowsDarkMode : AppLayout.WindowsLightMode);
                Window window = new Window { Width = 300, Height = 200, Content = control };
                window.Show();
                Dispatcher.UIThread.RunJobs();
                Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window).Dispose();
                App.ApplyLayout(AppLayout.WindowsLightMode);
                Dispatcher.UIThread.RunJobs();

                using Avalonia.Media.Imaging.WriteableBitmap frame = Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window);
                using Avalonia.Platform.ILockedFramebuffer buffer = frame.Lock();
                byte[] pixels = new byte[buffer.RowBytes * frame.PixelSize.Height];
                System.Runtime.InteropServices.Marshal.Copy(buffer.Address, pixels, 0, pixels.Length);
                window.Close();
                return pixels;
            }

            FileSystemEntry root = ChartCaptures.LoadFixture();
            System.Func<Avalonia.Controls.Control>[] controls =
            {
                () => { EntryTree tree = new EntryTree(); tree.SetRootEntry(root); return tree; },
                () => { Treemap map = new Treemap(); map.SetRootEntry(root); map.SetEntry(root); return map; },
                () => { EntryTable table = new EntryTable(); table.SetEntry(root); return table; },
                () => new ToolbarIcon { Kind = ToolbarIconKind.Analysis },
            };

            try
            {
                foreach (System.Func<Avalonia.Controls.Control> control in controls)
                {
                    Assert.Equal(Pixels(control(), false), Pixels(control(), true));
                }
            }
            finally
            {
                App.ApplyLayout(AppLayout.WindowsDarkMode);
            }
        }

        // An exception in an async event handler (export, save...) is
        // logged and shown, not a crash.
        [AvaloniaFact]
        public void An_unhandled_exception_on_the_ui_thread_is_logged_and_shown()
        {
            System.Exception shown = null;
            App.KeepRunningOnUnhandledExceptions(exception => shown = exception);
            AppAlertLog.DeleteAll();

            Dispatcher.UIThread.Post(async () =>
            {
                await System.Threading.Tasks.Task.Yield();
                throw new System.IO.IOException("disk full");
            });
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("disk full", shown?.Message);
            Assert.Contains(AppAlertLog.GetEntries(), entry => entry.Message == "disk full");
        }

        [AvaloniaFact]
        public void Texts_and_direction_follow_the_language_at_runtime()
        {
            MainWindow window = new MainWindow();
            window.Show();
            TextBlock status = window.FindControl<TextBlock>("DriveLabel");

            try
            {
                SwitchLanguage("en");
                Assert.Equal("Drive:", status.Text);
                Assert.Equal(FlowDirection.LeftToRight, window.FlowDirection);

                SwitchLanguage("de");
                Assert.Equal(LocalizationService.GetText("Toolbar.Drive"), status.Text);
                Assert.NotEqual("Drive:", status.Text);

                SwitchLanguage("ar");
                Assert.Equal(LocalizationService.GetText("Toolbar.Drive"), status.Text);
                Assert.NotEqual("Drive:", status.Text);
                Assert.Equal(FlowDirection.RightToLeft, window.FlowDirection);
            }
            finally
            {
                SwitchLanguage("en");
                window.Close();
            }
        }

        [AvaloniaTheory]
        [InlineData(AppLayout.WindowsLightMode, "Light", "#FFFFFFFF")]
        [InlineData(AppLayout.WindowsDarkMode, "Dark", "#FF202020")]
        public void Layout_setting_picks_the_theme_and_its_colors(AppLayout layout, string variant, string background)
        {
            MainWindow window = new MainWindow();
            window.Show();

            try
            {
                App.ApplyLayout(layout);
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(variant, window.ActualThemeVariant.Key);
                Assert.Equal(Color.Parse(background), ((ISolidColorBrush)window.Background).Color);
            }
            finally
            {
                App.ApplyLayout(AppLayout.WindowsDarkMode);
                window.Close();
            }
        }

        private static void SwitchLanguage(string code)
        {
            LocalizationService.Load(code);
            Dispatcher.UIThread.RunJobs();
        }
    }
}
