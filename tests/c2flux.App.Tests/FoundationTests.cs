using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Xunit;

namespace c2flux.AppTests
{
    public class FoundationTests
    {
        [AvaloniaFact]
        public void Texts_and_direction_follow_the_language_at_runtime()
        {
            MainWindow window = new MainWindow();
            window.Show();
            TextBlock status = window.FindControl<TextBlock>("StatusText");

            try
            {
                SwitchLanguage("en");
                Assert.Equal("Ready", status.Text);
                Assert.Equal(FlowDirection.LeftToRight, window.FlowDirection);

                SwitchLanguage("de");
                Assert.Equal("Bereit", status.Text);

                SwitchLanguage("ar");
                Assert.Equal(LocalizationService.GetText("Common.Ready"), status.Text);
                Assert.NotEqual("Ready", status.Text);
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
