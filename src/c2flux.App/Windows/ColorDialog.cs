using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Rendering;

namespace c2flux
{
    // Picks an opaque color: null when cancelled.
    public static class ColorDialog
    {
        public static async Task<Color?> ShowAsync(Window owner, Color color)
        {
            ColorDialogWindow dialog = new ColorDialogWindow(owner.Title, color);
            await dialog.ShowDialog(owner);
            return dialog.Result;
        }
    }

    internal sealed class ColorDialogWindow : Window
    {
        public ColorDialogWindow(string title, Color color)
        {
            Title = title;
            Width = 340;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            CanMinimize = false;
            CanMaximize = false;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            Editor = new ColorEditor(color);
            Ok = new Button { Content = LocalizationService.GetText("Common.OK"), Width = 90, Height = 32, IsDefault = true, HorizontalContentAlignment = HorizontalAlignment.Center, Classes = { "ant", "dialog", "primary" } };
            Cancel = new Button { Content = LocalizationService.GetText("Common.Cancel"), Width = 90, Height = 32, IsCancel = true, HorizontalContentAlignment = HorizontalAlignment.Center, Classes = { "ant", "dialog" } };
            Ok.Click += (_, _) =>
            {
                Result = Editor.Color;
                Close();
            };
            Cancel.Click += (_, _) => Close();
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 14,
                Children =
                {
                    Editor,
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 10, Children = { Ok, Cancel } },
                },
            };
        }

        public ColorEditor Editor { get; }
        public Button Ok { get; }
        public Button Cancel { get; }
        public Color? Result { get; private set; }
    }

    // Saturation and brightness in a square, hue in a bar, and the numbers.
    // Hue and saturation live apart from the color: black has neither, and
    // moving through it must not lose them.
    internal sealed class ColorEditor : UserControl
    {
        private readonly Border _swatch = new Border { Width = 48, Height = 48, CornerRadius = new CornerRadius(8) };
        private double _hue;
        private double _saturation;
        private double _value;

        public ColorEditor(Color color)
        {
            Area = new SaturationValueArea(this) { Height = 220 };
            Hue = new HueBar(this) { Height = 16, VerticalAlignment = VerticalAlignment.Center };
            HexBox = Field("HEX", 7);
            RedBox = Field("R", 3);
            GreenBox = Field("G", 3);
            BlueBox = Field("B", 3);
            HexBox.TextChanged += (_, _) => OnHexTyped();

            foreach (TextBox box in new[] { RedBox, GreenBox, BlueBox })
            {
                box.AddHandler(TextInputEvent, (_, e) => e.Handled = e.Text == null || !e.Text.All(char.IsAsciiDigit), RoutingStrategies.Tunnel);
                box.TextChanged += (_, _) => OnChannelTyped(box);
            }

            foreach (TextBox box in new[] { HexBox, RedBox, GreenBox, BlueBox })
            {
                // Whatever was left half typed gives way to the color.
                box.LostFocus += (_, _) => Refresh(null);
            }

            Grid.SetColumn(Hue, 1);
            Hue.Margin = new Thickness(12, 0, 0, 0);
            Grid fields = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,*,*,*"), RowDefinitions = new RowDefinitions("Auto,Auto") };
            AddField(fields, "HEX", HexBox, 0);
            AddField(fields, "R", RedBox, 1);
            AddField(fields, "G", GreenBox, 2);
            AddField(fields, "B", BlueBox, 3);

            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    Area,
                    new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Children = { _swatch, Hue } },
                    fields,
                },
            };
            SetColor(color, null);
        }

        public Color Color { get; private set; }
        public double HueDegrees => _hue;
        public double Saturation => _saturation;
        public double Brightness => _value;
        public SaturationValueArea Area { get; }
        public HueBar Hue { get; }
        public TextBox HexBox { get; }
        public TextBox RedBox { get; }
        public TextBox GreenBox { get; }
        public TextBox BlueBox { get; }

        public void SetHue(double hue)
        {
            _hue = Math.Clamp(hue, 0, 360);
            FromHsv();
        }

        public void SetSaturationValue(double saturation, double value)
        {
            _saturation = Math.Clamp(saturation, 0, 1);
            _value = Math.Clamp(value, 0, 1);
            FromHsv();
        }

        private static TextBox Field(string name, int maxLength)
        {
            TextBox box = new TextBox { MaxLength = maxLength, Height = 36, TextAlignment = TextAlignment.Center, Classes = { "ant" } };
            AutomationProperties.SetName(box, name);
            return box;
        }

        private static void AddField(Grid grid, string label, TextBox box, int column)
        {
            TextBlock text = new TextBlock { Text = label, FontSize = 11, Margin = new Thickness(6, 0, 0, 2), Classes = { "ant", "antd" } };
            Grid.SetColumn(text, column);
            Grid.SetColumn(box, column);
            Grid.SetRow(box, 1);
            grid.Children.Add(text);
            grid.Children.Add(box);
        }

        private void FromHsv()
        {
            Color = Opaque(HsvColor.FromHsv(_hue, _saturation, _value).ToRgb());
            Refresh(null);
        }

        private static Color Opaque(Color color) => Color.FromRgb(color.R, color.G, color.B);

        private void SetColor(Color color, TextBox source)
        {
            Color = Opaque(color);
            HsvColor hsv = Color.ToHsv();
            _value = hsv.V;

            if (hsv.V > 0)
            {
                _saturation = hsv.S;
            }

            if (hsv.V > 0 && hsv.S > 0)
            {
                _hue = hsv.H;
            }

            Refresh(source);
        }

        // Every view but the box being typed in, which would lose its caret.
        private void Refresh(TextBox source)
        {
            _swatch.Background = new SolidColorBrush(Color);
            SetText(HexBox, source, "#" + Color.R.ToString("X2") + Color.G.ToString("X2") + Color.B.ToString("X2"));
            SetText(RedBox, source, Color.R.ToString(CultureInfo.InvariantCulture));
            SetText(GreenBox, source, Color.G.ToString(CultureInfo.InvariantCulture));
            SetText(BlueBox, source, Color.B.ToString(CultureInfo.InvariantCulture));
            Area.InvalidateVisual();
            Hue.InvalidateVisual();
        }

        private static void SetText(TextBox box, TextBox source, string text)
        {
            if (box != source)
            {
                box.Text = text;
            }
        }

        private void OnHexTyped()
        {
            string text = HexBox.Text?.Trim().TrimStart('#');

            if (text?.Length == 6 && text.All(char.IsAsciiHexDigit))
            {
                uint rgb = uint.Parse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                Typed(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb), HexBox);
            }
        }

        private void OnChannelTyped(TextBox box)
        {
            if (int.TryParse(box.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int typed))
            {
                byte channel = (byte)Math.Min(typed, 255);
                Typed(Color.FromRgb(box == RedBox ? channel : Color.R, box == GreenBox ? channel : Color.G, box == BlueBox ? channel : Color.B), box);
            }
        }

        // TextChanged comes later, for our own writes too. Those say the color
        // already is: taking them would round the hue through RGB.
        private void Typed(Color color, TextBox box)
        {
            if (color != Color)
            {
                SetColor(color, box);
            }
        }
    }

    // Left to right, white to the hue; top to bottom, to black.
    internal sealed class SaturationValueArea : Control, ICustomHitTest
    {
        private const double Step = 0.01;
        private readonly ColorEditor _editor;

        public SaturationValueArea(ColorEditor editor)
        {
            _editor = editor;
            Focusable = true;
            ClipToBounds = false;
            AutomationProperties.SetName(this, LocalizationService.GetText("Color.SaturationBrightness"));
            GotFocus += (_, _) => InvalidateVisual();
            LostFocus += (_, _) => InvalidateVisual();
        }

        // All of the box: hits otherwise follow the rounded shape drawn.
        public bool HitTest(Point point) => new Rect(Bounds.Size).Contains(point);

        public override void Render(DrawingContext context)
        {
            Rect area = new Rect(Bounds.Size);
            Color hue = HsvColor.FromHsv(_editor.HueDegrees, 1, 1).ToRgb();
            context.DrawRectangle(new SolidColorBrush(hue), null, area, 8, 8);
            context.DrawRectangle(Gradient(new RelativePoint(1, 0, RelativeUnit.Relative), Color.FromArgb(255, 255, 255, 255), Color.FromArgb(0, 255, 255, 255)), null, area, 8, 8);
            context.DrawRectangle(Gradient(new RelativePoint(0, 1, RelativeUnit.Relative), Color.FromArgb(0, 0, 0, 0), Color.FromArgb(255, 0, 0, 0)), null, area, 8, 8);

            Point thumb = new Point(_editor.Saturation * area.Width, (1 - _editor.Brightness) * area.Height);
            double radius = IsFocused ? 9 : 7;
            context.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)), 1), thumb, radius + 1.5, radius + 1.5);
            context.DrawEllipse(null, new Pen(Brushes.White, 2), thumb, radius, radius);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            Focus();
            e.Pointer.Capture(this);
            MoveTo(e.GetPosition(this));
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);

            if (e.Pointer.Captured == this)
            {
                MoveTo(e.GetPosition(this));
            }
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            e.Pointer.Capture(null);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            double saturation = e.Key == Key.Left ? -Step : e.Key == Key.Right ? Step : 0;
            double value = e.Key == Key.Down ? -Step : e.Key == Key.Up ? Step : 0;

            if (saturation != 0 || value != 0)
            {
                _editor.SetSaturationValue(_editor.Saturation + saturation, _editor.Brightness + value);
                e.Handled = true;
            }
        }

        private static LinearGradientBrush Gradient(RelativePoint end, Color from, Color to) => new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = end,
            GradientStops = { new GradientStop(from, 0), new GradientStop(to, 1) },
        };

        private void MoveTo(Point point) =>
            _editor.SetSaturationValue(point.X / Bounds.Width, 1 - (point.Y / Bounds.Height));
    }

    // The hues around the wheel, with the thumb kept inside the ends.
    internal sealed class HueBar : Control, ICustomHitTest
    {
        private const double ThumbRadius = 8;
        private readonly ColorEditor _editor;

        public HueBar(ColorEditor editor)
        {
            _editor = editor;
            Focusable = true;
            AutomationProperties.SetName(this, LocalizationService.GetText("Color.Hue"));
            GotFocus += (_, _) => InvalidateVisual();
            LostFocus += (_, _) => InvalidateVisual();
        }

        public double PositionOf(double hue) => ThumbRadius + (hue / 360 * (Bounds.Width - (2 * ThumbRadius)));

        // All of the box: hits otherwise follow the rounded shape drawn.
        public bool HitTest(Point point) => new Rect(Bounds.Size).Contains(point);

        public override void Render(DrawingContext context)
        {
            LinearGradientBrush brush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            };

            for (int stop = 0; stop <= 6; stop++)
            {
                brush.GradientStops.Add(new GradientStop(HsvColor.FromHsv(stop * 60 % 360, 1, 1).ToRgb(), stop / 6.0));
            }

            Rect bar = new Rect(Bounds.Size);
            context.DrawRectangle(brush, null, bar, bar.Height / 2, bar.Height / 2);

            Point thumb = new Point(PositionOf(_editor.HueDegrees), bar.Height / 2);
            double radius = IsFocused ? ThumbRadius + 2 : ThumbRadius;
            context.DrawEllipse(new SolidColorBrush(HsvColor.FromHsv(_editor.HueDegrees, 1, 1).ToRgb()), new Pen(Brushes.White, 2), thumb, radius, radius);
            context.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)), 1), thumb, radius + 1.5, radius + 1.5);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            Focus();
            e.Pointer.Capture(this);
            MoveTo(e.GetPosition(this));
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);

            if (e.Pointer.Captured == this)
            {
                MoveTo(e.GetPosition(this));
            }
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            e.Pointer.Capture(null);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            double step = e.Key == Key.Left ? -1 : e.Key == Key.Right ? 1 : 0;

            if (step != 0)
            {
                _editor.SetHue(_editor.HueDegrees + step);
                e.Handled = true;
            }
        }

        private void MoveTo(Point point) =>
            _editor.SetHue((point.X - ThumbRadius) / (Bounds.Width - (2 * ThumbRadius)) * 360);
    }
}
