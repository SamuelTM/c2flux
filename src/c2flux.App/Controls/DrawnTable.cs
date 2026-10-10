using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace c2flux
{
    public sealed class TableColumn<TRow>
    {
        public TableColumn(string title, Func<TRow, string> text)
        {
            Title = title;
            Text = text;
        }

        public string Title { get; set; }

        // Cell text; also what the tooltip shows when the cell is cut.
        public Func<TRow, string> Text { get; }

        // Defaults to the text.
        public Func<TRow, IComparable> SortKey { get; set; }

        // Width in pixels (the minimum when Percent is set); the last visible
        // column also takes what is left.
        public double Width { get; set; } = 120;

        // Share of the table's width, 0..1, as AntdUI's "22%" widths.
        public double Percent { get; set; }

        // Sized to the header and the cells' text instead of Width.
        public bool AutoWidth { get; set; }

        public HorizontalAlignment Alignment { get; set; } = HorizontalAlignment.Left;

        public bool Visible { get; set; } = true;

        public bool Sortable { get; set; } = true;

        // Draws the cell instead of its text (cell bounds, row, selected).
        public Action<DrawingContext, Rect, TRow, bool> Paint { get; set; }
    }

    // The tables of the WinForms app (AntdUI.Table): a fixed header with sort
    // arrows, resizable columns, rows of 30 px with hover and selection, a
    // rounded border around the rows that exist, and scrolling. Only the
    // visible rows are drawn.
    public sealed class DrawnTable<TRow> : Grid
    {
        private readonly TableCanvas _canvas;
        private readonly ScrollBar _verticalScrollBar = new ScrollBar { Orientation = Orientation.Vertical };
        private readonly ScrollBar _horizontalScrollBar = new ScrollBar { Orientation = Orientation.Horizontal };

        public DrawnTable()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto");
            RowDefinitions = new RowDefinitions("*,Auto");
            _canvas = new TableCanvas(this);
            Grid.SetColumn(_verticalScrollBar, 1);
            Grid.SetRow(_horizontalScrollBar, 1);
            Children.Add(_canvas);
            Children.Add(_verticalScrollBar);
            Children.Add(_horizontalScrollBar);
            _verticalScrollBar.ValueChanged += (_, _) => _canvas.InvalidateVisual();
            _horizontalScrollBar.ValueChanged += (_, _) => _canvas.InvalidateVisual();
        }

        public List<TableColumn<TRow>> Columns { get; } = new List<TableColumn<TRow>>();

        public event Action<TRow> SelectionChanged;

        // Any press on a row (left or right button), after it is selected.
        public event Action<TRow, PointerPressedEventArgs> RowPressed;

        // Double click or Enter.
        public event Action<TRow> RowActivated;

        public IReadOnlyList<TRow> Items => _canvas.SourceRows;

        public TRow SelectedItem => _canvas.SelectedRow;

        public void SetItems(IEnumerable<TRow> rows)
        {
            _canvas.SetRows(rows?.ToList() ?? new List<TRow>());
        }

        // Selects row (or nothing for default) and scrolls it into view.
        public void Select(TRow row)
        {
            _canvas.SelectRow(row, raiseEvent: false, scrollIntoView: true);
        }

        // After changing Columns (titles, visibility, widths).
        public void RefreshColumns()
        {
            _canvas.RefreshLayout();
        }

        internal double VerticalOffset => _verticalScrollBar.IsVisible ? _verticalScrollBar.Value : 0;

        internal double HorizontalOffset => _horizontalScrollBar.IsVisible ? _horizontalScrollBar.Value : 0;

        internal void UpdateScrollBars(double contentWidth, double contentHeight, double viewportWidth, double viewportHeight)
        {
            Configure(_verticalScrollBar, contentHeight, viewportHeight, TableCanvas.RowHeight);
            Configure(_horizontalScrollBar, contentWidth, viewportWidth, 24);
        }

        internal void ScrollVerticallyTo(double value)
        {
            _verticalScrollBar.Value = Math.Clamp(value, 0, _verticalScrollBar.Maximum);
            _canvas.InvalidateVisual();
        }

        internal void ScrollVerticallyBy(double delta) => ScrollVerticallyTo(_verticalScrollBar.Value + delta);

        internal void RaiseSelectionChanged(TRow row) => SelectionChanged?.Invoke(row);

        internal void RaiseRowPressed(TRow row, PointerPressedEventArgs e) => RowPressed?.Invoke(row, e);

        internal void RaiseRowActivated(TRow row) => RowActivated?.Invoke(row);

        private static void Configure(ScrollBar scrollBar, double content, double viewport, double smallChange)
        {
            double maximum = Math.Max(0, content - viewport);
            scrollBar.Maximum = maximum;
            scrollBar.ViewportSize = viewport;
            scrollBar.LargeChange = viewport;
            scrollBar.SmallChange = smallChange;
            scrollBar.Value = Math.Clamp(scrollBar.Value, 0, maximum);
            scrollBar.IsVisible = maximum > 0;
        }

        private enum SortDirection
        {
            None,
            Ascending,
            Descending
        }

        private sealed class TableCanvas : DrawnControl
        {
            public const double RowHeight = 30;
            private const double HeaderHeight = 32;
            private const double CellPadding = 8;
            private const double SortIconWidth = 7;
            private const double SortIconRightGap = 10;
            private const double ResizeGrip = 4;
            private const double MinimumColumnWidth = 30;

            private readonly DrawnTable<TRow> _owner;
            private List<TRow> _sourceRows = new List<TRow>();
            private List<TRow> _rows = new List<TRow>();
            private readonly List<(TableColumn<TRow> Column, double X, double Width)> _layout = new List<(TableColumn<TRow>, double, double)>();
            private TableColumn<TRow> _sortColumn;
            private SortDirection _sortDirection;
            private int _selectedIndex = -1;
            private int _hoverIndex = -1;
            private (TableColumn<TRow> Column, double StartX, double StartWidth)? _resize;
            private string _toolTipText;

            public TableCanvas(DrawnTable<TRow> owner)
            {
                _owner = owner;
                Focusable = true;
                ClipToBounds = true;
            }

            public List<TRow> SourceRows => _sourceRows;

            public TRow SelectedRow => _selectedIndex >= 0 && _selectedIndex < _rows.Count ? _rows[_selectedIndex] : default;

            public void SetRows(List<TRow> rows)
            {
                TRow selected = SelectedRow;
                _sourceRows = rows;
                ApplySort();
                _selectedIndex = selected == null ? -1 : _rows.IndexOf(selected);
                _hoverIndex = -1;
                RefreshLayout();
            }

            public void SelectRow(TRow row, bool raiseEvent, bool scrollIntoView)
            {
                int index = row == null ? -1 : _rows.IndexOf(row);
                SetSelectedIndex(index, raiseEvent);

                if (scrollIntoView && index >= 0)
                {
                    EnsureVisible(index);
                }
            }

            public void RefreshLayout()
            {
                LayoutColumns();
                UpdateScrollBars();
                InvalidateVisual();
            }

            protected override void OnSizeChanged(SizeChangedEventArgs e)
            {
                base.OnSizeChanged(e);
                RefreshLayout();
            }

            // ----- layout -------------------------------------------------

            // Columns start at x = 2 (inside the border) and end with their
            // separator; the last visible one fills the remaining width.
            private void LayoutColumns()
            {
                _layout.Clear();
                List<TableColumn<TRow>> visible = _owner.Columns.Where(column => column.Visible).ToList();
                double x = 2;

                foreach (TableColumn<TRow> column in visible)
                {
                    double width = column.AutoWidth
                        ? MeasureAutoWidth(column)
                        : Math.Max(column.Width, Math.Floor(column.Percent * (Bounds.Width - 4)));
                    _layout.Add((column, x, width));
                    x += width;
                }

                if (_layout.Count > 0)
                {
                    double available = Bounds.Width - 2 - x;

                    if (available > 0)
                    {
                        var last = _layout[^1];
                        _layout[^1] = (last.Column, last.X, last.Width + available);
                    }
                }
            }

            private double MeasureAutoWidth(TableColumn<TRow> column)
            {
                // Header: padding, title, then the sort arrows and their gap
                // to the separator, as DrawHeader lays them out.
                double header = CellPadding + MeasureHeader(column.Title) +
                    (column.Sortable ? 4 + SortIconWidth + SortIconRightGap : CellPadding) + 1;
                double cells = _rows.Count == 0
                    ? 0
                    : _rows.Take(500).Max(row => Math.Ceiling(CreateText(column.Text(row) ?? string.Empty, null).WidthIncludingTrailingWhitespace)) + CellPadding * 2 + 1;
                return Math.Ceiling(Math.Max(header, cells));
            }

            private double ContentWidth => _layout.Count == 0 ? 0 : _layout[^1].X + _layout[^1].Width + 2;

            private double ViewportRowsHeight => Math.Max(0, Bounds.Height - HeaderHeight - 2);

            private void UpdateScrollBars()
            {
                _owner.UpdateScrollBars(ContentWidth, _rows.Count * RowHeight, Bounds.Width, ViewportRowsHeight);
            }

            // ----- drawing ------------------------------------------------

            public override void Render(DrawingContext context)
            {
                double width = Bounds.Width;
                context.FillRectangle(Resource("BackgroundPrimaryBrush"), new Rect(Bounds.Size));

                if (_layout.Count == 0 || width < 4)
                {
                    return;
                }

                double offsetX = _owner.HorizontalOffset;
                double offsetY = _owner.VerticalOffset;
                double rowsTop = HeaderHeight + 2;
                double contentBottom = Math.Min(Bounds.Height - 1, rowsTop + _rows.Count * RowHeight - offsetY);
                contentBottom = Math.Max(rowsTop - 1, contentBottom);
                Rect frame = new Rect(1, 1, width - 2, contentBottom - 1);
                IBrush border = Resource("BorderBrush");

                using (context.PushClip(new RoundedRect(frame, 6)))
                {
                    context.FillRectangle(Resource("BackgroundSecondaryBrush"), new Rect(1, 1, width - 2, HeaderHeight));
                    DrawRows(context, rowsTop, contentBottom, offsetX, offsetY, border);

                    using (context.PushClip(new Rect(0, 0, width, HeaderHeight + 1)))
                    {
                        DrawHeader(context, offsetX);
                    }

                    context.FillRectangle(border, new Rect(1, HeaderHeight + 1, width - 2, 1));

                    foreach (var (_, x, columnWidth) in _layout.Take(_layout.Count - 1))
                    {
                        context.FillRectangle(border, new Rect(x + columnWidth - 1 - offsetX, 1, 1, contentBottom - 1));
                    }
                }

                // A 1 px rounded outline centered on the frame's edge, as
                // AntdUI draws it: two half-bright pixels on each side.
                context.DrawRectangle(null, new Pen(border, 1), new RoundedRect(frame, 6));
            }

            private void DrawHeader(DrawingContext context, double offsetX)
            {
                IBrush foreground = Foreground;
                Color iconColor = (foreground as ISolidColorBrush)?.Color ?? Colors.White;
                IBrush idleIcon = new SolidColorBrush(Color.FromArgb(64, iconColor.R, iconColor.G, iconColor.B));
                IBrush activeIcon = Resource("AccentBrush");

                foreach (var (column, columnX, columnWidth) in _layout)
                {
                    double x = columnX - offsetX;
                    double textRight = x + columnWidth - CellPadding - 1;

                    if (column.Sortable)
                    {
                        double iconRight = x + columnWidth - SortIconRightGap;
                        DrawSortIcon(
                            context,
                            iconRight - SortIconWidth,
                            HeaderHeight / 2 + 1,
                            _sortColumn == column && _sortDirection == SortDirection.Ascending ? activeIcon : idleIcon,
                            _sortColumn == column && _sortDirection == SortDirection.Descending ? activeIcon : idleIcon);
                        textRight = iconRight - SortIconWidth - 4;
                    }

                    FormattedText title = CreateText(column.Title ?? string.Empty, foreground);
                    title.SetFontWeight(FontWeight.Bold);
                    DrawAligned(context, title, new Rect(x + CellPadding, 1, Math.Max(0, textRight - x - CellPadding), HeaderHeight), column.Alignment);
                }
            }

            // Two small triangles, up over down.
            private static void DrawSortIcon(DrawingContext context, double left, double centerY, IBrush up, IBrush down)
            {
                StreamGeometry Triangle(double tipY, double baseY)
                {
                    StreamGeometry geometry = new StreamGeometry();

                    using (StreamGeometryContext path = geometry.Open())
                    {
                        path.BeginFigure(new Point(left + SortIconWidth / 2, tipY), true);
                        path.LineTo(new Point(left + SortIconWidth, baseY));
                        path.LineTo(new Point(left, baseY));
                        path.EndFigure(true);
                    }

                    return geometry;
                }

                context.DrawGeometry(up, null, Triangle(centerY - 6, centerY - 1.5));
                context.DrawGeometry(down, null, Triangle(centerY + 6, centerY + 1.5));
            }

            private void DrawRows(DrawingContext context, double rowsTop, double contentBottom, double offsetX, double offsetY, IBrush border)
            {
                if (_rows.Count == 0)
                {
                    return;
                }

                using DrawingContext.PushedState clip = context.PushClip(new Rect(0, rowsTop, Bounds.Width, Math.Max(0, contentBottom - rowsTop)));
                int first = Math.Max(0, (int)(offsetY / RowHeight));
                IBrush hover = Resource("SurfaceHighlightBrush");
                IBrush selected = Resource("AccentBrush");

                for (int index = first; index < _rows.Count; index++)
                {
                    double top = rowsTop + index * RowHeight - offsetY;

                    if (top >= contentBottom)
                    {
                        break;
                    }

                    bool isSelected = index == _selectedIndex;
                    Rect rowBounds = new Rect(1, top, Bounds.Width - 2, RowHeight - 1);

                    if (isSelected)
                    {
                        context.FillRectangle(selected, rowBounds);
                    }
                    else if (index == _hoverIndex)
                    {
                        context.FillRectangle(hover, rowBounds);
                    }

                    foreach (var (column, columnX, columnWidth) in _layout)
                    {
                        Rect cell = new Rect(columnX - offsetX, top, columnWidth - 1, RowHeight - 1);

                        if (column.Paint != null)
                        {
                            column.Paint(context, cell, _rows[index], isSelected);
                        }
                        else
                        {
                            FormattedText text = CreateText(column.Text(_rows[index]) ?? string.Empty, isSelected ? Brushes.White : Foreground);
                            DrawAligned(context, text, cell.Deflate(new Thickness(CellPadding, 0)), column.Alignment);
                        }
                    }

                    if (index < _rows.Count - 1)
                    {
                        context.FillRectangle(border, new Rect(1, top + RowHeight - 1, Bounds.Width - 2, 1));
                    }
                }
            }

            // One line, cut with an ellipsis, vertically centered.
            private static void DrawAligned(DrawingContext context, FormattedText text, Rect bounds, HorizontalAlignment alignment)
            {
                if (bounds.Width <= 0)
                {
                    return;
                }

                text.MaxTextWidth = bounds.Width;
                text.MaxLineCount = 1;
                text.Trimming = TextTrimming.CharacterEllipsis;
                text.TextAlignment = alignment switch
                {
                    HorizontalAlignment.Right => TextAlignment.Right,
                    HorizontalAlignment.Center => TextAlignment.Center,
                    _ => TextAlignment.Left,
                };
                context.DrawText(text, new Point(bounds.X, bounds.Y + Math.Round((bounds.Height - text.Height) / 2)));
            }

            private double MeasureHeader(string title)
            {
                FormattedText text = CreateText(title ?? string.Empty, null);
                text.SetFontWeight(FontWeight.Bold);
                return Math.Ceiling(text.WidthIncludingTrailingWhitespace);
            }

            // ----- sorting ------------------------------------------------

            // AntdUI cycles ascending, descending, original order.
            private void ToggleSort(TableColumn<TRow> column)
            {
                if (_sortColumn != column)
                {
                    _sortColumn = column;
                    _sortDirection = SortDirection.Ascending;
                }
                else
                {
                    _sortDirection = _sortDirection == SortDirection.Ascending ? SortDirection.Descending : SortDirection.None;
                }

                TRow selected = SelectedRow;
                ApplySort();
                _selectedIndex = selected == null ? -1 : _rows.IndexOf(selected);
                InvalidateVisual();
            }

            private void ApplySort()
            {
                if (_sortColumn == null || _sortDirection == SortDirection.None)
                {
                    _rows = new List<TRow>(_sourceRows);
                    return;
                }

                Func<TRow, IComparable> key = _sortColumn.SortKey ?? (row => _sortColumn.Text(row));
                _rows = (_sortDirection == SortDirection.Ascending
                    ? _sourceRows.OrderBy(key)
                    : _sourceRows.OrderByDescending(key)).ToList();
            }

            // ----- input --------------------------------------------------

            private int RowAt(Point point)
            {
                double rowsTop = HeaderHeight + 2;

                if (point.Y < rowsTop)
                {
                    return -1;
                }

                int index = (int)((point.Y - rowsTop + _owner.VerticalOffset) / RowHeight);
                return index >= 0 && index < _rows.Count ? index : -1;
            }

            private TableColumn<TRow> ColumnEdgeAt(Point point)
            {
                if (point.Y > HeaderHeight + 1)
                {
                    return null;
                }

                double x = point.X + _owner.HorizontalOffset;
                return _layout.Take(_layout.Count - 1).FirstOrDefault(item => Math.Abs(item.X + item.Width - 1 - x) <= ResizeGrip).Column;
            }

            private (TableColumn<TRow> Column, double X, double Width) ColumnAt(double x)
            {
                x += _owner.HorizontalOffset;
                return _layout.FirstOrDefault(item => x >= item.X && x < item.X + item.Width);
            }

            protected override void OnPointerPressed(PointerPressedEventArgs e)
            {
                base.OnPointerPressed(e);
                Focus();

                Point point = e.GetPosition(this);
                TableColumn<TRow> edge = ColumnEdgeAt(point);

                if (edge != null && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                {
                    _resize = (edge, point.X, _layout.First(item => item.Column == edge).Width);
                    e.Pointer.Capture(this);
                    e.Handled = true;
                    return;
                }

                if (point.Y <= HeaderHeight + 1)
                {
                    TableColumn<TRow> column = ColumnAt(point.X).Column;

                    if (column != null && column.Sortable && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                    {
                        ToggleSort(column);
                    }

                    e.Handled = true;
                    return;
                }

                int index = RowAt(point);

                if (index < 0)
                {
                    return;
                }

                SetSelectedIndex(index, raiseEvent: true);
                _owner.RaiseRowPressed(_rows[index], e);

                if (e.ClickCount == 2 && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                {
                    _owner.RaiseRowActivated(_rows[index]);
                }

                e.Handled = true;
            }

            protected override void OnPointerMoved(PointerEventArgs e)
            {
                base.OnPointerMoved(e);
                Point point = e.GetPosition(this);

                if (_resize is { } resize)
                {
                    resize.Column.AutoWidth = false;
                    resize.Column.Width = Math.Max(MinimumColumnWidth, resize.StartWidth + point.X - resize.StartX);
                    RefreshLayout();
                    return;
                }

                Cursor = ColumnEdgeAt(point) != null ? new Cursor(StandardCursorType.SizeWestEast) : Cursor.Default;

                int hover = RowAt(point);

                if (hover != _hoverIndex)
                {
                    _hoverIndex = hover;
                    InvalidateVisual();
                }

                UpdateToolTip(point, hover);
            }

            protected override void OnPointerReleased(PointerReleasedEventArgs e)
            {
                base.OnPointerReleased(e);

                if (_resize != null)
                {
                    _resize = null;
                    e.Pointer.Capture(null);
                }
            }

            protected override void OnPointerExited(PointerEventArgs e)
            {
                base.OnPointerExited(e);
                _hoverIndex = -1;
                ToolTip.SetTip(this, null);
                _toolTipText = null;
                InvalidateVisual();
            }

            protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
            {
                base.OnPointerWheelChanged(e);
                _owner.ScrollVerticallyBy(-e.Delta.Y * RowHeight * 3);
                e.Handled = true;
            }

            // ShowTip: the whole text of a cell that does not fit.
            private void UpdateToolTip(Point point, int rowIndex)
            {
                string tip = null;
                var (column, _, width) = ColumnAt(point.X);

                if (rowIndex >= 0 && column != null && column.Paint == null)
                {
                    string text = column.Text(_rows[rowIndex]) ?? string.Empty;

                    if (CreateText(text, null).WidthIncludingTrailingWhitespace > width - 1 - CellPadding * 2)
                    {
                        tip = text;
                    }
                }

                if (tip != _toolTipText)
                {
                    _toolTipText = tip;
                    ToolTip.SetTip(this, tip);
                }
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                base.OnKeyDown(e);

                if (_rows.Count == 0)
                {
                    return;
                }

                int page = Math.Max(1, (int)(ViewportRowsHeight / RowHeight) - 1);
                int target = e.Key switch
                {
                    Key.Up => Math.Max(0, _selectedIndex - 1),
                    Key.Down => Math.Min(_rows.Count - 1, _selectedIndex + 1),
                    Key.PageUp => Math.Max(0, _selectedIndex - page),
                    Key.PageDown => Math.Min(_rows.Count - 1, _selectedIndex + page),
                    Key.Home => 0,
                    Key.End => _rows.Count - 1,
                    _ => -2,
                };

                if (target >= 0)
                {
                    SetSelectedIndex(target, raiseEvent: true);
                    EnsureVisible(target);
                    e.Handled = true;
                }
                else if (e.Key == Key.Enter && _selectedIndex >= 0)
                {
                    _owner.RaiseRowActivated(_rows[_selectedIndex]);
                    e.Handled = true;
                }
            }

            private void SetSelectedIndex(int index, bool raiseEvent)
            {
                if (index == _selectedIndex)
                {
                    return;
                }

                _selectedIndex = index;
                InvalidateVisual();

                if (raiseEvent)
                {
                    _owner.RaiseSelectionChanged(SelectedRow);
                }
            }

            private void EnsureVisible(int index)
            {
                double top = index * RowHeight;
                double offset = _owner.VerticalOffset;

                if (top < offset)
                {
                    _owner.ScrollVerticallyTo(top);
                }
                else if (top + RowHeight > offset + ViewportRowsHeight)
                {
                    _owner.ScrollVerticallyTo(top + RowHeight - ViewportRowsHeight);
                }
            }

            protected override AutomationPeer OnCreateAutomationPeer()
            {
                return new TablePeer(this);
            }

            // Screen readers get a data grid named after the selected row.
            private sealed class TablePeer : ControlAutomationPeer
            {
                public TablePeer(TableCanvas owner)
                    : base(owner)
                {
                }

                protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.DataGrid;

                protected override string GetNameCore()
                {
                    TableCanvas table = (TableCanvas)Owner;
                    TRow row = table.SelectedRow;

                    return row == null
                        ? base.GetNameCore()
                        : string.Join(", ", table._layout.Select(item => item.Column.Title + ": " + item.Column.Text(row)));
                }
            }
        }
    }
}
