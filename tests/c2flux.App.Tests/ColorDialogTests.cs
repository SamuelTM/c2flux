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
using Xunit;

namespace c2flux.AppTests
{
    public class ColorDialogTests
    {
        private static ColorDialogWindow Show(Color color)
        {
            ColorDialogWindow window = new ColorDialogWindow("t", color);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            return window;
        }

        private static void Type(TextBox box, string text)
        {
            box.Text = text;
            Dispatcher.UIThread.RunJobs();
        }

        private static void Click(Window window, Control control, double x, double y)
        {
            Point point = control.TranslatePoint(new Point(x, y), window).Value;
            window.MouseMove(point);
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }

        // What comes in goes out unchanged: no rounding through hue and back.
        [AvaloniaTheory]
        [InlineData(0x97, 0x51, 0xF2)]
        [InlineData(0x01, 0x02, 0x03)]
        [InlineData(0, 0, 0)]
        [InlineData(255, 255, 255)]
        [InlineData(128, 128, 128)]
        public void A_color_comes_back_as_it_went_in(byte r, byte g, byte b)
        {
            ColorDialogWindow window = Show(Color.FromRgb(r, g, b));

            try
            {
                Assert.Equal(r.ToString(), window.Editor.RedBox.Text);
                Assert.Equal(g.ToString(), window.Editor.GreenBox.Text);
                Assert.Equal(b.ToString(), window.Editor.BlueBox.Text);
                window.Ok.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(Color.FromRgb(r, g, b), window.Result);
            }
            finally
            {
                window.Close();
            }
        }

        [AvaloniaFact]
        public void Alpha_never_survives()
        {
            ColorDialogWindow window = Show(Color.FromArgb(40, 10, 20, 30));

            try
            {
                Assert.Equal(Color.FromRgb(10, 20, 30), window.Editor.Color);
            }
            finally
            {
                window.Close();
            }
        }

        [AvaloniaFact]
        public void Cancel_gives_nothing()
        {
            ColorDialogWindow window = Show(Colors.Red);
            window.Cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Null(window.Result);
        }

        // The corners of the square, under a real pointer.
        [AvaloniaFact]
        public void The_corners_of_the_square_are_white_the_hue_and_black()
        {
            ColorDialogWindow window = Show(Color.FromRgb(255, 0, 0));

            try
            {
                SaturationValueArea area = window.Editor.Area;
                Click(window, area, 0, 0);
                Assert.Equal(Colors.White, window.Editor.Color);
                Click(window, area, area.Bounds.Width - 0.01, 0);
                Assert.Equal(255, window.Editor.Color.R);
                Assert.True(window.Editor.Color.G < 2 && window.Editor.Color.B < 2);
                Click(window, area, 0, area.Bounds.Height - 0.01);
                Assert.True(window.Editor.Color.R < 2 && window.Editor.Color.G < 2 && window.Editor.Color.B < 2);
            }
            finally
            {
                window.Close();
            }
        }

        // Black and white have no hue of their own to give back.
        [AvaloniaFact]
        public void The_hue_survives_black_and_white()
        {
            ColorDialogWindow window = Show(Color.FromRgb(0, 200, 0));

            try
            {
                double hue = window.Editor.HueDegrees;
                window.Editor.SetSaturationValue(1, 0);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(hue, window.Editor.HueDegrees);
                window.Editor.SetSaturationValue(0, 1);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(hue, window.Editor.HueDegrees);
                window.Editor.SetSaturationValue(1, 1);
                Assert.Equal(Color.FromRgb(0, 255, 0), window.Editor.Color);
            }
            finally
            {
                window.Close();
            }
        }

        [AvaloniaFact]
        public void The_ends_of_the_hue_bar_are_the_same_red()
        {
            ColorDialogWindow window = Show(Color.FromRgb(0, 200, 0));

            try
            {
                HueBar bar = window.Editor.Hue;
                Click(window, bar, bar.PositionOf(240), bar.Bounds.Height / 2);
                Assert.Equal(240, window.Editor.HueDegrees, 0);
                Assert.Equal(Color.FromRgb(0, 0, 200), window.Editor.Color);
                Click(window, bar, 0, bar.Bounds.Height / 2);
                Assert.Equal(Color.FromRgb(200, 0, 0), window.Editor.Color);
                Click(window, bar, bar.Bounds.Width - 0.01, bar.Bounds.Height / 2);
                Assert.Equal(Color.FromRgb(200, 0, 0), window.Editor.Color);
            }
            finally
            {
                window.Close();
            }
        }

        [AvaloniaFact]
        public void Typed_hex_and_channels_change_the_rest()
        {
            ColorDialogWindow window = Show(Colors.Black);

            try
            {
                Type(window.Editor.HexBox, "#9751F2");
                Assert.Equal(Color.FromRgb(0x97, 0x51, 0xF2), window.Editor.Color);
                Assert.Equal("151", window.Editor.RedBox.Text);
                Assert.Equal("81", window.Editor.GreenBox.Text);
                Assert.Equal("242", window.Editor.BlueBox.Text);

                Type(window.Editor.GreenBox, "0");
                Assert.Equal("#9700F2", window.Editor.HexBox.Text);

                Type(window.Editor.BlueBox, "999");
                Assert.Equal(255, window.Editor.Color.B);

                // Half typed: nothing yet, and the color wins when focus leaves.
                Type(window.Editor.HexBox, "#12");
                Assert.Equal(Color.FromRgb(0x97, 0, 255), window.Editor.Color);
                Type(window.Editor.GreenBox, "");
                Assert.Equal(0, window.Editor.Color.G);
                window.Editor.HexBox.Focus();
                window.Editor.RedBox.Focus();
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("#9700FF", window.Editor.HexBox.Text);
            }
            finally
            {
                window.Close();
            }
        }

        [AvaloniaFact]
        public void Channels_take_only_digits()
        {
            ColorDialogWindow window = Show(Colors.Black);

            try
            {
                window.Editor.RedBox.Focus();
                window.Editor.RedBox.SelectAll();

                foreach (char key in "-1a2")
                {
                    window.KeyTextInput(key.ToString());
                }

                Dispatcher.UIThread.RunJobs();
                Assert.Equal("12", window.Editor.RedBox.Text);
                Assert.Equal(12, window.Editor.Color.R);
            }
            finally
            {
                window.Close();
            }
        }

        [AvaloniaFact]
        public void Arrow_keys_move_the_thumbs()
        {
            ColorDialogWindow window = Show(Color.FromRgb(128, 64, 64));

            try
            {
                double hue = window.Editor.HueDegrees;
                double saturation = window.Editor.Saturation;
                window.Editor.Hue.Focus();
                window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(hue + 1, window.Editor.HueDegrees, 3);
                window.Editor.Area.Focus();
                window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
                Assert.Equal(saturation + 0.01, window.Editor.Saturation, 3);
            }
            finally
            {
                window.Close();
            }
        }
    }
}
