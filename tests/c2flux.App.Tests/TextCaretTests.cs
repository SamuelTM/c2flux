using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace c2flux.AppTests
{
    // Avalonia places the caret as if the text started at the left, so a
    // TextBox with TextAlignment center or right shows it away from the text.
    public class TextCaretTests
    {
        // Private, but it is where the caret is drawn: nothing public says so.
        private static readonly FieldInfo CaretBounds = typeof(TextPresenter).GetField("_caretBounds", BindingFlags.NonPublic | BindingFlags.Instance);

        private static void AssertCaretAtEnd(Window window, TextBox box)
        {
            box.Focus();
            box.CaretIndex = box.Text.Length;
            Dispatcher.UIThread.RunJobs();
            window.KeyPressQwerty(PhysicalKey.Backspace, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            TextPresenter presenter = box.GetVisualDescendants().OfType<TextPresenter>().First();
            TextLine line = presenter.TextLayout.TextLines[0];
            double caret = ((Rect)CaretBounds.GetValue(presenter)).X;
            Assert.Equal(line.Start + line.WidthIncludingTrailingWhitespace, caret, 1);
        }

        [AvaloniaFact]
        public void The_caret_follows_the_text_in_the_color_fields()
        {
            ColorDialogWindow window = new ColorDialogWindow(Color.FromRgb(0x07, 0xBD, 0x47));
            window.Show();
            Dispatcher.UIThread.RunJobs();

            try
            {
                AssertCaretAtEnd(window, window.Editor.HexBox);
                AssertCaretAtEnd(window, window.Editor.GreenBox);
            }
            finally
            {
                window.Close();
            }
        }

        [AvaloniaTheory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(4)]
        public void The_caret_follows_the_text_in_the_number_fields(int page)
        {
            SettingsWindow window = new SettingsWindow(new AppSettings { AutoSaveLog = true, StorageHistoryDetailsEnabled = true, StorageHistoryDetailsAutoPurgeEnabled = true }, windowsOptions: true);
            window.Show();

            try
            {
                window.ShowPage(page);
                Dispatcher.UIThread.RunJobs();
                TextBox box = window.GetVisualDescendants().OfType<TextBox>().First(item => item.IsEffectivelyVisible && item.IsEffectivelyEnabled && !string.IsNullOrEmpty(item.Text) && item.Classes.Contains("ant"));
                AssertCaretAtEnd(window, box);
            }
            finally
            {
                window.Close();
            }
        }
    }
}
