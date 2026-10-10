using Avalonia;
using Avalonia.Media;

namespace c2flux
{
    // The AntdUI.Progress of the status bar: a rounded bar with its text
    // ("42.0 % | 3.1 s") centered over it.
    public sealed class ScanProgressBar : DrawnControl
    {
        private double _value;
        private string _text = string.Empty;
        private IBrush _fill;

        // 0..1
        public double Value
        {
            get => _value;
            set
            {
                _value = System.Math.Clamp(value, 0, 1);
                InvalidateVisual();
            }
        }

        public string Text
        {
            get => _text;
            set
            {
                _text = value ?? string.Empty;
                InvalidateVisual();
            }
        }

        // Accent while scanning, orange while saving history details.
        public IBrush Fill
        {
            get => _fill;
            set
            {
                _fill = value;
                InvalidateVisual();
            }
        }

        public override void Render(DrawingContext context)
        {
            Rect track = new Rect(0, (Bounds.Height - 16) / 2, Bounds.Width, 16);
            context.DrawRectangle(Resource("BackgroundTertiaryBrush"), null, new RoundedRect(track, 4));

            if (_value > 0)
            {
                context.DrawRectangle(_fill ?? Resource("AccentBrush"), null, new RoundedRect(track.WithWidth(System.Math.Max(8, track.Width * _value)), 4));
            }

            FormattedText text = CreateText(_text, Foreground);
            context.DrawText(text, new Point((Bounds.Width - text.Width) / 2, track.Y + (track.Height - text.Height) / 2));
        }
    }
}
