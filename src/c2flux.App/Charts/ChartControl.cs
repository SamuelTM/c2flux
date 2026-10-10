using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;

namespace c2flux
{
    // What the WinForms charts share: an entry to show, areas that map points
    // to entries (filled while rendering), a date tooltip, the "Open in
    // Explorer" context menu and text in the window's font.
    public abstract class ChartControl : Control
    {
        private readonly List<(Func<Point, bool> Contains, FileSystemEntry Entry)> _hitAreas =
            new List<(Func<Point, bool>, FileSystemEntry)>();
        private FileSystemEntry _hoveredEntry;

        protected ChartControl()
        {
            ClipToBounds = true;
        }

        protected FileSystemEntry Entry { get; private set; }

        public void SetEntry(FileSystemEntry entry)
        {
            Entry = entry;
            _hoveredEntry = null;
            ToolTip.SetTip(this, null);
            InvalidateVisual();
        }

        public sealed override void Render(DrawingContext context)
        {
            _hitAreas.Clear();
            RenderChart(context);
        }

        protected abstract void RenderChart(DrawingContext context);

        // Format key of the tooltip ({0} created, {1} new line, ... as in
        // the WinForms chart); null for no tooltip.
        protected abstract string FormatToolTip(FileSystemEntry entry, DateTime created, DateTime modified, DateTime accessed);

        protected void AddHitArea(Rect bounds, FileSystemEntry entry)
        {
            AddHitArea(bounds.Contains, entry);
        }

        protected void AddHitArea(Func<Point, bool> contains, FileSystemEntry entry)
        {
            if (entry != null)
            {
                _hitAreas.Add((contains, entry));
            }
        }

        internal FileSystemEntry HitTest(Point point)
        {
            return _hitAreas.FirstOrDefault(area => area.Contains(point)).Entry;
        }

        protected IBrush Foreground =>
            this.FindResource(ActualThemeVariant, "TextPrimaryBrush") as IBrush ?? Brushes.White;

        protected FormattedText CreateText(string text, IBrush brush)
        {
            return new FormattedText(
                text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(FontFamily.Default),
                GetValue(TextElement.FontSizeProperty),
                brush);
        }

        // WinForms Font.Height of Segoe UI (line spacing 1.33 em: 16 px at
        // 12 px), so rows keep the original height with any font.
        protected double FontHeight => Math.Ceiling(GetValue(TextElement.FontSizeProperty) * 1.33);

        // TextRenderer pads text by about a sixth of the line height.
        protected double TextPadding => Math.Ceiling(FontHeight / 6);

        // TextRenderer.MeasureText: text width plus padding on both sides.
        protected double MeasureWidth(string text)
        {
            return Math.Ceiling(CreateText(text, null).WidthIncludingTrailingWhitespace + TextPadding * 2);
        }

        // TextRenderer.DrawText with Left | VerticalCenter | EndEllipsis.
        protected void DrawTextLine(DrawingContext context, string text, Rect bounds, IBrush brush)
        {
            double width = bounds.Width - TextPadding * 2;

            if (width <= 0)
            {
                return;
            }

            FormattedText formatted = CreateText(text, brush);
            formatted.MaxTextWidth = width;
            formatted.MaxLineCount = 1;
            formatted.Trimming = TextTrimming.CharacterEllipsis;
            context.DrawText(formatted, new Point(bounds.X + TextPadding, bounds.Y + (bounds.Height - formatted.Height) / 2));
        }

        protected void DrawNoData(DrawingContext context)
        {
            FormattedText text = CreateText(LocalizationService.GetText("Chart.NoData"), Foreground);
            context.DrawText(text, new Point((Bounds.Width - text.Width) / 2, (Bounds.Height - text.Height) / 2));
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);

            FileSystemEntry entry = HitTest(e.GetPosition(this));

            if (entry == _hoveredEntry)
            {
                return;
            }

            _hoveredEntry = entry;
            ToolTip.SetTip(this, CreateToolTip(entry));
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);
            _hoveredEntry = null;
            ToolTip.SetTip(this, null);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);

            if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
            {
                return;
            }

            FileSystemEntry entry = HitTest(e.GetPosition(this));

            if (entry == null || string.IsNullOrWhiteSpace(entry.FullPath))
            {
                return;
            }

            MenuItem reveal = new MenuItem { Header = LocalizationService.GetText("Context.OpenInExplorer") };
            reveal.Click += (_, _) => Reveal(entry.FullPath);
            new ContextMenu { ItemsSource = new[] { reveal } }.Open(this);
            e.Handled = true;
        }

        // Folders open in the file manager, files are selected in it.
        private static void Reveal(string path)
        {
            if (Directory.Exists(path))
            {
                FileManager.Open(path);
            }
            else if (File.Exists(path))
            {
                FileManager.Reveal(path);
            }
        }

        private string CreateToolTip(FileSystemEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.FullPath))
            {
                return null;
            }

            string path = entry.FullPath;

            try
            {
                if (entry.IsDirectory ? !Directory.Exists(path) : !File.Exists(path))
                {
                    return null;
                }

                return entry.IsDirectory
                    ? FormatToolTip(entry, Directory.GetCreationTime(path), Directory.GetLastWriteTime(path), Directory.GetLastAccessTime(path))
                    : FormatToolTip(entry, File.GetCreationTime(path), File.GetLastWriteTime(path), File.GetLastAccessTime(path));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return null;
            }
        }

        // Vertical gradient over the given bounds, as GDI+'s
        // LinearGradientBrush(bounds, top, bottom, Vertical).
        protected static IBrush FamilyGradient(Color color, Rect bounds)
        {
            return new LinearGradientBrush
            {
                StartPoint = new RelativePoint(bounds.TopLeft, RelativeUnit.Absolute),
                EndPoint = new RelativePoint(bounds.BottomLeft, RelativeUnit.Absolute),
                GradientStops =
                {
                    new GradientStop(ChartColors.Lighten(color, ChartColors.GradientTopFactor), 0),
                    new GradientStop(ChartColors.Darken(color, ChartColors.GradientBottomFactor), 1),
                },
            };
        }
    }
}
