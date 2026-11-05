using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
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
