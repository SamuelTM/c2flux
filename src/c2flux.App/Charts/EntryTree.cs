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
    // Port of the WinForms TreeEntrySizeBarView: the scanned folders as a tree
    // whose rows carry a bar proportional to the share of the parent. Only
    // the visible rows are drawn, so it copes with very large trees.
    public sealed class EntryTree : ScrollViewer
    {
        private readonly EntryTreeCanvas _canvas;

        public EntryTree()
        {
            _canvas = new EntryTreeCanvas(this);
            Content = _canvas;
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
            ScrollChanged += (_, _) => _canvas.InvalidateVisual();
        }

        protected override Type StyleKeyOverride => typeof(ScrollViewer);

        public event Action<FileSystemEntry> SelectedEntryChanged;

        // The entry under the pointer and the press (button, position).
        public event Action<FileSystemEntry, PointerPressedEventArgs> EntryPressed;

        public double RowHeight
        {
            get => _canvas.RowHeight;
            set => _canvas.RowHeight = Math.Max(16, value);
        }

        public FileSystemEntry SelectedEntry => _canvas.SelectedEntry;

        public void SetRootEntry(FileSystemEntry rootEntry) => _canvas.SetRootEntry(rootEntry, true);

        public void UpdateRootEntry(FileSystemEntry rootEntry) => _canvas.SetRootEntry(rootEntry, false);

        public void ClearEntries() => _canvas.ClearEntries();

        public bool SelectEntry(FileSystemEntry entry) => _canvas.SelectEntry(entry);

        public FileSystemEntry GetRootEntry(FileSystemEntry entry) => _canvas.FindRootNodeForEntry(entry)?.Entry;

        public bool RemoveRootEntry(FileSystemEntry entry) => _canvas.RemoveRootEntry(entry);

        internal void RaiseSelectedEntryChanged(FileSystemEntry entry) => SelectedEntryChanged?.Invoke(entry);

        internal void RaiseEntryPressed(FileSystemEntry entry, PointerPressedEventArgs e) => EntryPressed?.Invoke(entry, e);

        internal void ScrollToRow(double rowTop, double rowBottom)
        {
            double top = Offset.Y;
            double bottom = top + Viewport.Height;

            if (rowTop < top)
            {
                Offset = Offset.WithY(rowTop);
            }
            else if (rowBottom > bottom)
            {
                Offset = Offset.WithY(rowBottom - Viewport.Height);
            }
        }
    }

    internal sealed class EntryTreeCanvas : DrawnControl
    {
        private const double LevelIndent = 24;
        private const double GlyphSize = 9;
        private const double GlyphLeftPadding = 4;
        private const double IconLeftOffset = 20;
        private const double TextLeftOffset = 40;
        private const double RightPadding = 1;

        // The WinForms bar color: translucent periwinkle over the background.
        private static readonly Color BarColor = Color.FromArgb(90, 130, 120, 255);

        private readonly EntryTree _owner;
        private readonly List<Node> _rootNodes = new List<Node>();
        private readonly List<Node> _visibleNodes = new List<Node>();
        private readonly HashSet<string> _expandedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Volume sizes by root path, read again on each SetRootEntry.
        private Dictionary<string, long> _volumeSizeByRoot;
        private Node _selectedNode;
        private double _rowHeight = 22;
        private double _virtualWidth;

        public EntryTreeCanvas(EntryTree owner)
        {
            _owner = owner;
            Focusable = true;
        }

        public double RowHeight
        {
            get => _rowHeight;
            set
            {
                _rowHeight = value;
                RebuildVisibleNodes();
            }
        }

        public FileSystemEntry SelectedEntry => _selectedNode?.Entry;

        public void ClearEntries()
        {
            _rootNodes.Clear();
            _visibleNodes.Clear();
            _expandedKeys.Clear();
            _selectedNode = null;
            RebuildVisibleNodes();
        }

        public void SetRootEntry(FileSystemEntry rootEntry, bool forceSelectionToRoot)
        {
            if (rootEntry == null)
            {
                ClearEntries();
                return;
            }

            _volumeSizeByRoot = null;
            string previousSelectedKey = _selectedNode?.Key;
            string rootKey = GetEntryKey(rootEntry, null);
            Node rootNode = _rootNodes.FirstOrDefault(node => string.Equals(node.Key, rootKey, StringComparison.OrdinalIgnoreCase));

            if (rootNode == null)
            {
                rootNode = new Node(rootEntry, null, 0, rootKey) { Expanded = true };
                _rootNodes.Add(rootNode);
                _expandedKeys.Add(rootKey);
            }

            SynchronizeNode(rootNode, rootEntry);
            RebuildVisibleNodes();

            Node newSelectedNode = null;

            if (!forceSelectionToRoot && !string.IsNullOrWhiteSpace(previousSelectedKey))
            {
                newSelectedNode = _visibleNodes.FirstOrDefault(node => string.Equals(node.Key, previousSelectedKey, StringComparison.OrdinalIgnoreCase));
            }

            newSelectedNode ??= rootNode;
            SelectNode(newSelectedNode, forceSelectionToRoot || newSelectedNode != _selectedNode);

            if (forceSelectionToRoot)
            {
                EnsureNodeVisible(newSelectedNode);
            }
        }

        public bool SelectEntry(FileSystemEntry entry)
        {
            Node rootNode = FindRootNodeForEntry(entry);

            if (rootNode == null)
            {
                return false;
            }

            string targetDirectoryPath = entry.IsDirectory ? entry.FullPath : Path.GetDirectoryName(entry.FullPath);

            if (string.IsNullOrWhiteSpace(targetDirectoryPath))
            {
                return false;
            }

            Node directoryNode = ExpandToDirectory(rootNode, targetDirectoryPath);

            if (directoryNode == null)
            {
                return false;
            }

            Node targetNode = directoryNode;

            if (!entry.IsDirectory)
            {
                directoryNode.Expanded = true;
                _expandedKeys.Add(directoryNode.Key);
                SynchronizeNode(directoryNode, directoryNode.Entry);

                string entryKey = GetEntryKey(entry, directoryNode);
                targetNode = directoryNode.Children.FirstOrDefault(child => string.Equals(child.Key, entryKey, StringComparison.OrdinalIgnoreCase));

                if (targetNode == null)
                {
                    targetNode = new Node(entry, directoryNode, directoryNode.Level + 1, entryKey);
                    directoryNode.Children.Add(targetNode);
                    directoryNode.Children.Sort(CompareNodes);
                }
            }

            RebuildVisibleNodes();
            SelectNode(targetNode, true);
            EnsureNodeVisible(targetNode);
            return true;
        }

        public bool RemoveRootEntry(FileSystemEntry entry)
        {
            Node rootNode = FindRootNodeForEntry(entry);

            if (rootNode == null)
            {
                return false;
            }

            bool selectedNodeRemoved = _selectedNode != null && IsSameOrDescendantPath(_selectedNode.Entry?.FullPath, rootNode.Entry?.FullPath);
            int rootIndex = _rootNodes.IndexOf(rootNode);

            RemoveExpandedKeys(rootNode);
            _rootNodes.Remove(rootNode);
            RebuildVisibleNodes();

            if (selectedNodeRemoved)
            {
                _selectedNode = null;

                if (_rootNodes.Count > 0)
                {
                    Node newSelectedNode = _rootNodes[Math.Min(rootIndex, _rootNodes.Count - 1)];
                    SelectNode(newSelectedNode, true);
                    EnsureNodeVisible(newSelectedNode);
                }
                else
                {
                    _owner.RaiseSelectedEntryChanged(null);
                }
            }

            InvalidateVisual();
            return true;
        }

        internal Node FindRootNodeForEntry(FileSystemEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.FullPath))
            {
                return null;
            }

            return _rootNodes.FirstOrDefault(rootNode =>
                !string.IsNullOrWhiteSpace(rootNode.Entry?.FullPath) &&
                IsSameOrDescendantPath(entry.FullPath, rootNode.Entry.FullPath));
        }

        // ----- layout and drawing ---------------------------------------

        protected override Size MeasureOverride(Size availableSize)
        {
            return new Size(_virtualWidth, _visibleNodes.Count * _rowHeight);
        }

        public override void Render(DrawingContext context)
        {
            // The canvas is as large as the whole tree; only the rows inside
            // the scroll viewport are drawn.
            Rect viewport = new Rect(_owner.Offset.X, _owner.Offset.Y, _owner.Viewport.Width, _owner.Viewport.Height);
            IBrush background = Resource("BackgroundPrimaryBrush");
            context.FillRectangle(background, viewport);
            IBrush barBrush = new SolidColorBrush(ChartColors.BlendLinear(BarColor, (background as ISolidColorBrush)?.Color ?? Colors.Black));

            int firstIndex = Math.Max(0, (int)(viewport.Y / _rowHeight));
            double right = viewport.Right;

            for (int index = firstIndex; index < _visibleNodes.Count && index * _rowHeight < viewport.Bottom; index++)
            {
                DrawNode(context, _visibleNodes[index], index * _rowHeight, viewport.X, right, barBrush);
            }
        }

        private void DrawNode(DrawingContext context, Node node, double y, double left, double right, IBrush barBrush)
        {
            bool selected = node == _selectedNode;

            if (selected)
            {
                context.FillRectangle(Resource("AccentBrush"), new Rect(left, y, right - left, _rowHeight));
            }
            else
            {
                double barLeft = GetTextLeft(node) - 2;
                double barRight = GetBarRight(node, right - RightPadding);

                if (barRight > barLeft)
                {
                    context.FillRectangle(barBrush, new Rect(barLeft, y + 2, barRight - barLeft, Math.Max(1, _rowHeight - 4)));
                }
            }

            if (CanExpand(node))
            {
                StatusSymbolRenderer.DrawTreeExpandGlyph(context, GetGlyphBounds(node, y), node.Expanded);
            }

            // shortcut: no file, folder or drive icons yet; they come with the
            // file icon service of phase 5 (IconLeftOffset keeps their place).
            if (IsSystemDirectory(node.Entry))
            {
                double markerSize = Math.Round(StatusSymbolRenderer.DefaultSymbolSize * 0.8);
                double iconTop = y + Math.Max(0, Math.Floor((_rowHeight - 16) / 2));
                StatusSymbolRenderer.DrawSymbol(
                    context,
                    new Rect(GetNodeLeft(node) + IconLeftOffset, iconTop + 5, markerSize, markerSize),
                    StatusSymbolKind.SystemDirectory);
            }

            double textLeft = GetTextLeft(node);
            DrawTextLine(
                context,
                GetNodeText(node.Entry),
                new Rect(textLeft, y, Math.Max(0, right - textLeft - 2), _rowHeight),
                selected ? Brushes.White : Foreground,
                padded: false);
        }

        // A bar spans the share of the parent's bar, so nested bars line up.
        private double GetBarRight(Node node, double rightLimit)
        {
            if (node.Parent == null)
            {
                return rightLimit;
            }

            double barLeft = GetTextLeft(node) - 2;
            double parentRight = GetBarRight(node.Parent, rightLimit);
            long totalSizeBytes = node.Parent.Entry?.SizeBytes ?? 0;

            if (totalSizeBytes <= 0)
            {
                totalSizeBytes = node.Entry?.SizeBytes ?? 0;
            }

            double percent = totalSizeBytes <= 0 ? 0 : Math.Max(0, Math.Min(1, (double)node.Entry.SizeBytes / totalSizeBytes));
            return barLeft + (int)(Math.Max(0, parentRight - barLeft) * percent);
        }

        private Rect GetGlyphBounds(Node node, double y)
        {
            return new Rect(GetNodeLeft(node) + GlyphLeftPadding, y + Math.Max(0, Math.Floor((_rowHeight - GlyphSize) / 2)), GlyphSize, GlyphSize);
        }

        private static double GetNodeLeft(Node node) => node.Level * LevelIndent;

        private static double GetTextLeft(Node node) => GetNodeLeft(node) + TextLeftOffset;

        // Volume roots show the volume's size, not the scanned bytes.
        private string GetNodeText(FileSystemEntry entry)
        {
            if (entry == null)
            {
                return string.Empty;
            }

            long displaySizeBytes = entry.SizeBytes;

            if (entry.IsDirectory && !string.IsNullOrWhiteSpace(entry.FullPath))
            {
                _volumeSizeByRoot ??= Volumes.List()
                    .GroupBy(volume => volume.RootPath, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.First().TotalBytes, StringComparer.OrdinalIgnoreCase);

                if (_volumeSizeByRoot.TryGetValue(entry.FullPath, out long volumeSize))
                {
                    displaySizeBytes = volumeSize;
                }
            }

            return SizeFormatter.Format(displaySizeBytes) + "  " + entry.Name;
        }

        private static bool IsSystemDirectory(FileSystemEntry entry)
        {
            return entry != null &&
                entry.IsDirectory &&
                (string.Equals(entry.Name, "System Volume Information", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(entry.Name, "$RECYCLE.BIN", StringComparison.OrdinalIgnoreCase));
        }

        // ----- input ------------------------------------------------------

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            Focus();

            Point point = e.GetPosition(this);
            int index = (int)(point.Y / _rowHeight);

            if (index < 0 || index >= _visibleNodes.Count)
            {
                return;
            }

            Node node = _visibleNodes[index];

            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && GetGlyphBounds(node, index * _rowHeight).Contains(point) && CanExpand(node))
            {
                ToggleNode(node);
                e.Handled = true;
                return;
            }

            SelectNode(node, true);
            _owner.RaiseEntryPressed(node.Entry, e);
            e.Handled = true;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (_visibleNodes.Count == 0)
            {
                return;
            }

            int selectedIndex = _selectedNode == null ? -1 : _visibleNodes.IndexOf(_selectedNode);

            if (selectedIndex < 0)
            {
                SelectNode(_visibleNodes[0], true);
                e.Handled = true;
                return;
            }

            Node target = e.Key switch
            {
                Key.Up when selectedIndex > 0 => _visibleNodes[selectedIndex - 1],
                Key.Down when selectedIndex < _visibleNodes.Count - 1 => _visibleNodes[selectedIndex + 1],
                Key.Home => _visibleNodes[0],
                Key.End => _visibleNodes[^1],
                Key.Left when !_selectedNode.Expanded => _selectedNode.Parent,
                _ => null,
            };

            if (target != null)
            {
                SelectNode(target, true);
                EnsureNodeVisible(target);
                e.Handled = true;
            }
            else if ((e.Key == Key.Right && CanExpand(_selectedNode) && !_selectedNode.Expanded) ||
                     (e.Key == Key.Left && _selectedNode.Expanded))
            {
                ToggleNode(_selectedNode);
                e.Handled = true;
            }
        }

        // ----- tree model -------------------------------------------------

        private Node ExpandToDirectory(Node currentNode, string targetDirectoryPath)
        {
            if (currentNode?.Entry == null || string.IsNullOrWhiteSpace(currentNode.Entry.FullPath))
            {
                return null;
            }

            if (string.Equals(NormalizeEntryPath(currentNode.Entry.FullPath), NormalizeEntryPath(targetDirectoryPath), StringComparison.OrdinalIgnoreCase))
            {
                return currentNode;
            }

            currentNode.Expanded = true;
            _expandedKeys.Add(currentNode.Key);
            SynchronizeNode(currentNode, currentNode.Entry);

            Node nextNode = currentNode.Children
                .Where(child => child.Entry != null && child.Entry.IsDirectory && !string.IsNullOrWhiteSpace(child.Entry.FullPath))
                .OrderByDescending(child => child.Entry.FullPath.Length)
                .FirstOrDefault(child => IsSameOrDescendantPath(targetDirectoryPath, child.Entry.FullPath));

            return nextNode == null ? null : ExpandToDirectory(nextNode, targetDirectoryPath);
        }

        private void SynchronizeNode(Node node, FileSystemEntry entry)
        {
            node.Entry = entry;

            if (!entry.IsDirectory)
            {
                node.Children.Clear();
                node.ChildrenLoaded = true;
                return;
            }

            if (!node.Expanded && node.Parent != null)
            {
                return;
            }

            Dictionary<string, Node> existingNodesByKey = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);

            foreach (Node childNode in node.Children)
            {
                existingNodesByKey[childNode.Key] = childNode;
            }

            node.Children.Clear();

            foreach (FileSystemEntry childEntry in GetSortedChildEntriesSnapshot(entry))
            {
                string childKey = GetEntryKey(childEntry, node);

                if (!existingNodesByKey.TryGetValue(childKey, out Node childNode))
                {
                    childNode = new Node(childEntry, node, node.Level + 1, childKey);
                }

                childNode.Entry = childEntry;
                childNode.Parent = node;
                childNode.Level = node.Level + 1;
                childNode.Expanded = _expandedKeys.Contains(childKey);
                node.Children.Add(childNode);

                if (childNode.Expanded)
                {
                    SynchronizeNode(childNode, childEntry);
                }
            }

            node.ChildrenLoaded = true;
        }

        private void RebuildVisibleNodes()
        {
            _visibleNodes.Clear();

            foreach (Node rootNode in _rootNodes)
            {
                AddVisibleNode(rootNode);
            }

            // As wide as the longest row plus 80 px, for the horizontal
            // scroll bar. Measuring every row took seconds with 200 000 rows;
            // the 20 longest texts of each level are enough to find it.
            _virtualWidth = _visibleNodes.Count == 0
                ? 0
                : _visibleNodes
                    .Select(node => (Node: node, Text: GetNodeText(node.Entry)))
                    .GroupBy(row => row.Node.Level)
                    .SelectMany(level => level.OrderByDescending(row => row.Text.Length).Take(20))
                    .Max(row => GetTextLeft(row.Node) + CreateText(row.Text, null).WidthIncludingTrailingWhitespace + 80);

            InvalidateMeasure();
            InvalidateVisual();
        }

        private void AddVisibleNode(Node node)
        {
            _visibleNodes.Add(node);

            if (!node.Expanded)
            {
                return;
            }

            if (!node.ChildrenLoaded)
            {
                SynchronizeNode(node, node.Entry);
            }

            foreach (Node childNode in node.Children)
            {
                AddVisibleNode(childNode);
            }
        }

        private void RemoveExpandedKeys(Node node)
        {
            _expandedKeys.Remove(node.Key);

            foreach (Node childNode in node.Children)
            {
                RemoveExpandedKeys(childNode);
            }
        }

        private void ToggleNode(Node node)
        {
            if (!CanExpand(node))
            {
                return;
            }

            node.Expanded = !node.Expanded;

            if (node.Expanded)
            {
                _expandedKeys.Add(node.Key);
                SynchronizeNode(node, node.Entry);
            }
            else
            {
                _expandedKeys.Remove(node.Key);
            }

            RebuildVisibleNodes();
        }

        private static bool CanExpand(Node node)
        {
            if (node?.Entry == null || !node.Entry.IsDirectory)
            {
                return false;
            }

            lock (node.Entry.Children)
            {
                return node.Entry.Children.Count > 0;
            }
        }

        private void SelectNode(Node node, bool raiseEvent)
        {
            if (node == null || _selectedNode == node)
            {
                return;
            }

            _selectedNode = node;

            if (raiseEvent)
            {
                _owner.RaiseSelectedEntryChanged(node.Entry);
            }

            InvalidateVisual();
        }

        private void EnsureNodeVisible(Node node)
        {
            int index = _visibleNodes.IndexOf(node);

            if (index >= 0)
            {
                _owner.ScrollToRow(index * _rowHeight, (index + 1) * _rowHeight);
            }
        }

        private static List<FileSystemEntry> GetSortedChildEntriesSnapshot(FileSystemEntry entry)
        {
            lock (entry.Children)
            {
                return entry.Children
                    .OrderByDescending(child => child.IsDirectory)
                    .ThenByDescending(child => child.SizeBytes)
                    .ThenBy(child => child.Name)
                    .ToList();
            }
        }

        private static int CompareNodes(Node left, Node right)
        {
            bool leftDirectory = left.Entry?.IsDirectory ?? false;
            bool rightDirectory = right.Entry?.IsDirectory ?? false;

            if (leftDirectory != rightDirectory)
            {
                return leftDirectory ? -1 : 1;
            }

            int sizeCompare = (right.Entry?.SizeBytes ?? 0).CompareTo(left.Entry?.SizeBytes ?? 0);

            return sizeCompare != 0
                ? sizeCompare
                : string.Compare(left.Entry?.Name, right.Entry?.Name, StringComparison.CurrentCultureIgnoreCase);
        }

        private static string GetEntryKey(FileSystemEntry entry, Node parentNode)
        {
            if (!string.IsNullOrWhiteSpace(entry.FullPath))
            {
                return entry.FullPath;
            }

            return (parentNode == null ? string.Empty : parentNode.Key) + Path.DirectorySeparatorChar + entry.Name;
        }

        internal static bool IsSameOrDescendantPath(string path, string parentPath)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(parentPath))
            {
                return false;
            }

            string normalizedPath = NormalizeEntryPath(path);
            string normalizedParent = NormalizeEntryPath(parentPath);

            if (string.Equals(normalizedPath, normalizedParent, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string prefix = normalizedParent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return normalizedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        // Drive roots keep their separator ("C:\"), the root "/" stays "/";
        // other paths lose a trailing separator.
        private static string NormalizeEntryPath(string path)
        {
            string normalized = EntryPaths.ToNativeSeparators(path.Trim());

            if (normalized.Length == 3 && normalized[1] == ':' && (normalized[2] == '\\' || normalized[2] == '/'))
            {
                return char.ToUpperInvariant(normalized[0]) + @":\";
            }

            string trimmed = normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return trimmed.Length == 0 ? normalized.Substring(0, 1) : trimmed;
        }

        internal sealed class Node
        {
            public Node(FileSystemEntry entry, Node parent, int level, string key)
            {
                Entry = entry;
                Parent = parent;
                Level = level;
                Key = key;
            }

            public FileSystemEntry Entry { get; set; }
            public Node Parent { get; set; }
            public int Level { get; set; }
            public string Key { get; }
            public bool Expanded { get; set; }
            public bool ChildrenLoaded { get; set; }
            public List<Node> Children { get; } = new List<Node>();
        }
    }
}
