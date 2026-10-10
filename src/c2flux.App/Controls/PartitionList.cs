using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace c2flux
{
    // Port of the WinForms partition panel (listViewPartitions and
    // PartitionGridController): one row per volume with its size, free space
    // and a bar of the free share.
    public sealed class PartitionList : DrawnControl
    {
        private const double HeaderHeight = 24;
        private const double RowHeight = 20;
        private const double SizeWidth = 70;
        private const double FreeWidth = 75;
        private const double FreePercentWidth = 60;
        private const int ProbeTimeoutMilliseconds = 3000;

        private readonly AppSettings _settings;
        private List<VolumeInfo> _volumes = new List<VolumeInfo>();
        private int _selectedIndex = -1;

        // Wide enough for the longest root path (24 px icon offset and
        // padding), at least 80 px.
        private double NameWidth => Math.Max(80, _volumes.Count == 0 ? 0 : _volumes.Max(volume => MeasureWidth(volume.RootPath)) + 28);

        public PartitionList(AppSettings settings)
        {
            _settings = settings;
            Focusable = true;
            ClipToBounds = true;
        }

        public event Action<string> SelectedVolumeChanged;

        // Volumes that take longer than 3 s to answer are left out, as in
        // WinForms.
        public async Task LoadAsync()
        {
            Task<IReadOnlyList<VolumeInfo>> probe = Task.Run(Volumes.List);
            Task finished = await Task.WhenAny(probe, Task.Delay(ProbeTimeoutMilliseconds));

            if (finished != probe)
            {
                AppAlertLog.AddWarning("Drive", "The volume list did not respond within 3 seconds.");
                return;
            }

            string selected = _selectedIndex >= 0 && _selectedIndex < _volumes.Count ? _volumes[_selectedIndex].RootPath : null;
            _volumes = (await probe).ToList();
            _selectedIndex = selected == null ? -1 : _volumes.FindIndex(volume => volume.RootPath == selected);
            InvalidateMeasure();
            InvalidateVisual();
        }

        public void Select(string rootPath)
        {
            _selectedIndex = _volumes.FindIndex(volume => string.Equals(volume.RootPath, rootPath, StringComparison.OrdinalIgnoreCase));
            InvalidateVisual();
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            return new Size(NameWidth + SizeWidth + FreeWidth + FreePercentWidth + 8, HeaderHeight + _volumes.Count * RowHeight);
        }

        public override void Render(DrawingContext context)
        {
            context.FillRectangle(Resource("BackgroundPrimaryBrush"), new Rect(Bounds.Size));
            IBrush foreground = Foreground;
            double x = 4;

            DrawTextLine(context, LocalizationService.GetText("Common.Name"), new Rect(x, 0, NameWidth, HeaderHeight), foreground);
            DrawRight(context, LocalizationService.GetText("Common.Size"), new Rect(x + NameWidth, 0, SizeWidth, HeaderHeight), foreground);
            DrawRight(context, LocalizationService.GetText("Common.Free"), new Rect(x + NameWidth + SizeWidth, 0, FreeWidth, HeaderHeight), foreground);
            DrawCentered(context, LocalizationService.GetText("Common.FreePercent"), new Rect(x + NameWidth + SizeWidth + FreeWidth, 0, FreePercentWidth, HeaderHeight), foreground);

            IBrush fill = new SolidColorBrush(GetFillColor());
            IBrush empty = Resource("BackgroundTertiaryBrush");
            Pen barBorder = new Pen(Resource("SurfaceHighlightBrush"), 1);

            for (int index = 0; index < _volumes.Count; index++)
            {
                VolumeInfo volume = _volumes[index];
                double top = HeaderHeight + index * RowHeight;
                bool selected = index == _selectedIndex;
                IBrush text = selected ? Brushes.White : foreground;

                if (selected)
                {
                    context.FillRectangle(Resource("AccentBrush"), new Rect(x, top, NameWidth + SizeWidth + FreeWidth + FreePercentWidth, RowHeight));
                }

                context.DrawImage(FileIconCache.Volume(volume.RootPath), new Rect(x + 4, top + Math.Floor((RowHeight - 16) / 2), 16, 16));
                DrawTextLine(context, volume.RootPath, new Rect(x + 24, top, NameWidth - 28, RowHeight), text);
                DrawRight(context, SizeFormatter.Format(volume.TotalBytes), new Rect(x + NameWidth, top, SizeWidth, RowHeight), text);
                DrawRight(context, SizeFormatter.Format(volume.FreeBytes), new Rect(x + NameWidth + SizeWidth, top, FreeWidth, RowHeight), text);

                int freePercent = volume.TotalBytes <= 0 ? 0 : (int)Math.Round(volume.FreeBytes * 100D / volume.TotalBytes);
                Rect bar = new Rect(x + NameWidth + SizeWidth + FreeWidth + 4, top + 2, FreePercentWidth - 8, RowHeight - 4);
                context.FillRectangle(empty, bar);
                context.FillRectangle(fill, bar.WithWidth(Math.Round(bar.Width * Math.Clamp(freePercent, 0, 100) / 100D)));
                context.DrawRectangle(null, barBorder, new Rect(bar.X + 0.5, bar.Y + 0.5, bar.Width, bar.Height));
                DrawCentered(context, freePercent + " %", bar, text);
            }
        }

        // The configurable fill color (Settings, UI tab) at its brightness.
        private Color GetFillColor()
        {
            bool dark = ActualThemeVariant != Avalonia.Styling.ThemeVariant.Light;
            uint argb = unchecked((uint)(dark ? _settings.PartitionFillColorDarkArgb : _settings.PartitionFillColorLightArgb));
            double factor = Math.Clamp(dark ? _settings.PartitionFillBrightnessDarkPercent : _settings.PartitionFillBrightnessLightPercent, 0, 200) / 100D;

            byte Scale(uint channel) => (byte)Math.Clamp((int)Math.Round(channel * factor), 0, 255);
            return Color.FromArgb((byte)(argb >> 24), Scale((argb >> 16) & 0xFF), Scale((argb >> 8) & 0xFF), Scale(argb & 0xFF));
        }

        private void DrawRight(DrawingContext context, string text, Rect bounds, IBrush brush)
        {
            double width = MeasureWidth(text);
            DrawTextLine(context, text, new Rect(bounds.Right - width, bounds.Y, width, bounds.Height), brush);
        }

        private void DrawCentered(DrawingContext context, string text, Rect bounds, IBrush brush)
        {
            double width = MeasureWidth(text);
            DrawTextLine(context, text, new Rect(bounds.X + (bounds.Width - width) / 2, bounds.Y, width, bounds.Height), brush);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            Focus();
            int index = (int)((e.GetPosition(this).Y - HeaderHeight) / RowHeight);

            if (e.GetPosition(this).Y >= HeaderHeight && index >= 0 && index < _volumes.Count)
            {
                _selectedIndex = index;
                InvalidateVisual();
                SelectedVolumeChanged?.Invoke(_volumes[index].RootPath);
                e.Handled = true;
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            int target = e.Key == Key.Up ? _selectedIndex - 1 : e.Key == Key.Down ? _selectedIndex + 1 : -2;

            if (target >= 0 && target < _volumes.Count)
            {
                _selectedIndex = target;
                InvalidateVisual();
                SelectedVolumeChanged?.Invoke(_volumes[target].RootPath);
                e.Handled = true;
            }
        }
    }
}
