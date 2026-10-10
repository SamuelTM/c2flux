using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace c2flux
{
    // What the WinForms charts share: an entry to show, areas that map points
    // to entries (filled while rendering), a tooltip and the "Open in
    // Explorer" context menu.
    public abstract class ChartControl : DrawnControl
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

        // Text shown while the pointer is over entry; null for none.
        protected abstract string GetToolTip(FileSystemEntry entry);

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
            ToolTip.SetTip(this, entry == null ? null : GetToolTip(entry));
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

        // The entry's dates read from disk, as the WinForms charts show them;
        // null when the entry no longer exists.
        protected static string FormatDates(FileSystemEntry entry, Func<DateTime, DateTime, DateTime, string> format)
        {
            string path = entry.FullPath;

            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            try
            {
                if (entry.IsDirectory ? !Directory.Exists(path) : !File.Exists(path))
                {
                    return null;
                }

                return entry.IsDirectory
                    ? format(Directory.GetCreationTime(path), Directory.GetLastWriteTime(path), Directory.GetLastAccessTime(path))
                    : format(File.GetCreationTime(path), File.GetLastWriteTime(path), File.GetLastAccessTime(path));
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
