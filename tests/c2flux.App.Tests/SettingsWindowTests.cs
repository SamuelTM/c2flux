using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace c2flux.AppTests
{
    public class SettingsWindowTests
    {
        // Text in a box narrower than itself gets cut: on every page, in every
        // language, with the Windows rows too.
        [AvaloniaTheory]
        [InlineData("en")]
        [InlineData("pt")]
        public void No_text_is_cut_on_any_page(string language)
        {
            LocalizationService.Load(language);
            SettingsWindow window = new SettingsWindow(new AppSettings(), windowsOptions: true);
            window.Show();
            List<string> cut = new List<string>();

            try
            {
                for (int page = 0; page < 5; page++)
                {
                    window.ShowPage(page);
                    Dispatcher.UIThread.RunJobs();

                    foreach (TextBlock text in window.GetVisualDescendants().OfType<TextBlock>().Where(text => !string.IsNullOrEmpty(text.Text) && text.IsVisible))
                    {
                        double natural = new TextBlock { Text = text.Text, FontFamily = text.FontFamily, FontSize = text.FontSize, FontWeight = text.FontWeight, FontStyle = text.FontStyle }.MeasureWidth();
                        Point? end = text.TranslatePoint(new Point(text.Bounds.Width, text.Bounds.Height), window);

                        if ((text.TextWrapping == TextWrapping.NoWrap && text.Bounds.Width + 0.5 < natural) ||
                            end == null || end.Value.X > window.Width ||
                            (end.Value.Y > window.Height && !text.GetVisualAncestors().OfType<ScrollViewer>().Any()))
                        {
                            cut.Add($"page {page}: \"{text.Text}\" needs {natural:F0}, has {text.Bounds.Width:F0}, ends at {end}");
                        }
                    }
                }
            }
            finally
            {
                window.Close();
                LocalizationService.Load("en");
            }

            Assert.True(cut.Count == 0, string.Join(Environment.NewLine, cut));
        }

        // Empty is no limit; 0 to 100 otherwise, whatever was saved or typed.
        [AvaloniaTheory]
        [InlineData(null, null, null)]
        [InlineData(5, null, 5)]
        [InlineData(100000, null, 100)]
        [InlineData(-3, null, 0)]
        [InlineData(null, "-43", 43)]
        [InlineData(null, "7a", 7)]
        [InlineData(5, "", null)]
        public async Task The_export_depth_stays_in_range(int? saved, string typed, int? expected)
        {
            AppSettings settings = new AppSettings { ExportMaxDepth = saved };
            SettingsWindow window = new SettingsWindow(settings, windowsOptions: true);
            window.Show();

            try
            {
                window.ShowPage(3);
                Dispatcher.UIThread.RunJobs();
                NumericUpDown depth = window.GetVisualDescendants().OfType<NumericUpDown>().Single();
                TextBox box = depth.GetVisualDescendants().OfType<TextBox>().Single();

                if (typed != null)
                {
                    box.Focus();
                    box.SelectAll();
                    window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);

                    foreach (char key in typed)
                    {
                        window.KeyTextInput(key.ToString());
                    }

                    Dispatcher.UIThread.RunJobs();
                }

                Assert.True(await window.TrySaveAsync());
                Assert.Equal(expected, settings.ExportMaxDepth);
            }
            finally
            {
                window.Close();
            }
        }

        // The arrows of the restyled spinner still change the value, up to the limit.
        [AvaloniaFact]
        public void The_export_depth_arrows_work()
        {
            SettingsWindow window = new SettingsWindow(new AppSettings { ExportMaxDepth = 99 }, windowsOptions: true);
            window.Show();

            try
            {
                window.ShowPage(3);
                Dispatcher.UIThread.RunJobs();
                NumericUpDown depth = window.GetVisualDescendants().OfType<NumericUpDown>().Single();
                Button up = depth.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "PART_IncreaseButton");
                Button down = depth.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "PART_DecreaseButton");

                up.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(100, depth.Value);
                Dispatcher.UIThread.RunJobs();
                Assert.False(up.IsEffectivelyEnabled);

                down.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(99, depth.Value);
            }
            finally
            {
                window.Close();
            }
        }

        // From empty, up starts at the minimum and down at the maximum; the
        // text has to show it too, not only the value.
        [AvaloniaTheory]
        [InlineData(null, "PART_IncreaseButton", 0)]
        [InlineData(null, "PART_DecreaseButton", 100)]
        [InlineData(100, "PART_IncreaseButton", 0)]
        [InlineData(100, "PART_DecreaseButton", 100)]
        [InlineData(0, "PART_IncreaseButton", 0)]
        [InlineData(0, "PART_DecreaseButton", 100)]
        [InlineData(5, "PART_IncreaseButton", 0)]
        [InlineData(5, "PART_DecreaseButton", 100)]
        public void The_export_depth_arrows_start_from_a_limit_when_empty(int? saved, string arrow, int expected)
        {
            SettingsWindow window = new SettingsWindow(new AppSettings { ExportMaxDepth = saved }, windowsOptions: true);
            window.Show();

            try
            {
                window.ShowPage(3);
                Dispatcher.UIThread.RunJobs();
                NumericUpDown depth = window.GetVisualDescendants().OfType<NumericUpDown>().Single();
                Button button = depth.GetVisualDescendants().OfType<Button>().Single(item => item.Name == arrow);
                TextBox box = depth.GetVisualDescendants().OfType<TextBox>().Single();
                box.Focus();
                box.SelectAll();
                window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);
                Dispatcher.UIThread.RunJobs();
                Assert.Null(depth.Value);
                Assert.True(button.IsEffectivelyEnabled, "the arrow is disabled while the field is empty");

                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(expected, depth.Value);
                Assert.Equal(expected.ToString(), depth.GetVisualDescendants().OfType<TextBox>().Single().Text);
            }
            finally
            {
                window.Close();
            }
        }

        [AvaloniaFact]
        public void The_history_help_is_only_text()
        {
            LocalizationService.Load("en");
            SettingsWindow window = new SettingsWindow(new AppSettings(), windowsOptions: true);
            window.Show();

            try
            {
                window.ShowPage(2);
                Dispatcher.UIThread.RunJobs();
                Button help = window.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "?"));
                Assert.Equal(LocalizationService.GetText("Settings.StorageHistoryDetailsHelp"), ToolTip.GetTip(help));
            }
            finally
            {
                window.Close();
            }
        }
    }

    internal static class TextMeasure
    {
        public static double MeasureWidth(this TextBlock text)
        {
            text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return text.DesiredSize.Width;
        }
    }
}
