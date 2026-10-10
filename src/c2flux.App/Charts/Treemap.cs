using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace c2flux
{
    // Port of the treemap canvas of the WinForms Chart_Treemap (the table
    // above it is ported with the other tables): a squarified treemap of the
    // entry, families of colors as in the sunburst, labels for large items and
    // "Other (n)" tiles for children too small to show.
    public sealed class Treemap : DrawnControl
    {
        private const double OuterPadding = 3;
        private const double FamilyHeaderHeight = 34;
        private const double MinimumRecursiveWidth = 24;
        private const double MinimumRecursiveHeight = 20;
        private const double MinimumLabelWidth = 64;
        private const double MinimumLabelHeight = 22;
        private const long MinimumLabelSizeBytes = 100L * 1024L * 1024L;
        private const double FamilySplitMinimumShare = 0.10D;
        private const double FamilyPromotionMinimumShare = 0.50D;
        private const double MinimumChildPixelArea = 30D;
        private const int MaximumVisibleChildren = 160;
        private const double LineHeight = 17;

        private readonly List<HitArea> _hitAreas = new List<HitArea>();
        private readonly DispatcherTimer _resizeTimer;
        private FileSystemEntry _entry;
        private FileSystemEntry _rootEntry;
        private HitArea _hoverHitArea;
        private TreemapNode _cachedRootNode;
        private RenderTargetBitmap _renderCache;
        private Size _renderCacheSize;
        private bool _renderCacheDirty = true;

        public Treemap()
        {
            ClipToBounds = true;

            // Like WinForms: while resizing, the old image is stretched; the
            // map is laid out again 90 ms after the size stops changing.
            _resizeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
            _resizeTimer.Tick += (_, _) =>
            {
                _resizeTimer.Stop();
                _renderCacheDirty = true;
                InvalidateVisual();
            };
        }

        // Left click on a file or folder.
        public event Action<FileSystemEntry> EntryActivated;

        // Double click on a folder.
        public event Action<FileSystemEntry> DirectoryZoomRequested;

        // Where files come from when they are not yet in Children (a scan in
        // progress fills the root's AllFiles first).
        public void SetRootEntry(FileSystemEntry rootEntry)
        {
            if (!ReferenceEquals(_rootEntry, rootEntry))
            {
                _rootEntry = rootEntry;
                InvalidateTreeCache();
            }

            ResetHover();
            InvalidateVisual();
        }

        public void SetEntry(FileSystemEntry entry)
        {
            if (!ReferenceEquals(_entry, entry))
            {
                _entry = entry;
                InvalidateTreeCache();
            }

            ResetHover();
            InvalidateVisual();
        }

        protected override void OnSizeChanged(SizeChangedEventArgs e)
        {
            base.OnSizeChanged(e);

            // The first layout has nothing to stretch: draw right away.
            if (_renderCache == null)
            {
                _renderCacheDirty = true;
                return;
            }

            _resizeTimer.Stop();
            _resizeTimer.Start();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            _resizeTimer.Stop();
            _renderCache?.Dispose();
            _renderCache = null;
            _renderCacheDirty = true;
        }

        public override void Render(DrawingContext context)
        {
            Rect clientBounds = new Rect(Bounds.Size);
            context.FillRectangle(Resource("BackgroundPrimaryBrush"), clientBounds);

            if (_entry == null)
            {
                DrawNoData(context);
                return;
            }

            if (_renderCache == null || _renderCacheDirty)
            {
                RebuildRenderCache();
            }

            if (_renderCache == null)
            {
                DrawNoData(context);
                return;
            }

            context.DrawImage(_renderCache, new Rect(_renderCacheSize), clientBounds);
            DrawHoverOverlay(context);
        }

        private void DrawNoData(DrawingContext context)
        {
            FormattedText text = CreateText(LocalizationService.GetText("Chart.NoData"), Foreground);
            context.DrawText(text, new Point((Bounds.Width - text.Width) / 2, (Bounds.Height - text.Height) / 2));
        }

        // ----- input ------------------------------------------------------

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);

            HitArea hitArea = GetHitArea(e.GetPosition(this));

            if (ReferenceEquals(_hoverHitArea, hitArea))
            {
                return;
            }

            _hoverHitArea = hitArea;
            ToolTip.SetTip(this, hitArea == null ? null : FormatToolTip(hitArea.Node));
            InvalidateVisual();
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);
            ResetHover();
            InvalidateVisual();
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);

            TreemapNode node = GetHitArea(e.GetPosition(this))?.Node;

            if (node == null)
            {
                return;
            }

            PointerPointProperties properties = e.GetCurrentPoint(this).Properties;

            if (properties.IsLeftButtonPressed)
            {
                if (e.ClickCount == 2)
                {
                    if (!node.IsAggregate && node.Entry != null && node.Entry.IsDirectory)
                    {
                        DirectoryZoomRequested?.Invoke(node.Entry);
                    }
                }
                else if (!node.IsAggregate && node.Entry != null)
                {
                    EntryActivated?.Invoke(node.Entry);
                }

                e.Handled = true;
            }
            else if (properties.IsRightButtonPressed)
            {
                ShowRevealMenu(node.IsAggregate ? node.AggregateParentEntry : node.Entry);
                e.Handled = true;
            }
        }

        private HitArea GetHitArea(Point location)
        {
            // The cache may be stretched while a resize is pending.
            if (_renderCacheSize.Width > 0 && _renderCacheSize.Height > 0 &&
                Bounds.Width > 0 && Bounds.Height > 0 &&
                _renderCacheSize != Bounds.Size)
            {
                location = new Point(
                    Math.Round(location.X * _renderCacheSize.Width / Bounds.Width),
                    Math.Round(location.Y * _renderCacheSize.Height / Bounds.Height));
            }

            // Children are added after their parents: the last match is the
            // deepest tile under the pointer.
            for (int index = _hitAreas.Count - 1; index >= 0; index--)
            {
                if (_hitAreas[index].Bounds.Contains(location))
                {
                    return _hitAreas[index];
                }
            }

            return null;
        }

        private static string FormatToolTip(TreemapNode node)
        {
            if (node.IsAggregate)
            {
                return node.DisplayName + Environment.NewLine + SizeFormatter.Format(GetNodeSize(node));
            }

            return node.Entry == null
                ? null
                : node.Entry.Name + Environment.NewLine + SizeFormatter.Format(node.Entry.SizeBytes) + Environment.NewLine + node.Entry.FullPath;
        }

        private void ResetHover()
        {
            _hoverHitArea = null;
            ToolTip.SetTip(this, null);
        }

        private void InvalidateTreeCache()
        {
            _cachedRootNode = null;
            _renderCacheDirty = true;
        }

        // ----- drawing ----------------------------------------------------

        private void RebuildRenderCache()
        {
            int width = (int)Bounds.Width;
            int height = (int)Bounds.Height;

            if (width <= 1 || height <= 1)
            {
                return;
            }

            double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
            _renderCache?.Dispose();
            _renderCache = new RenderTargetBitmap(
                new PixelSize((int)Math.Ceiling(width * scaling), (int)Math.Ceiling(height * scaling)),
                new Vector(96 * scaling, 96 * scaling));
            _renderCacheSize = new Size(width, height);
            _hitAreas.Clear();
            _renderCacheDirty = false;

            using DrawingContext context = _renderCache.CreateDrawingContext();
            // SmoothingMode.None: tiles and borders on whole pixels.
            using DrawingContext.PushedState aliased = context.PushRenderOptions(new RenderOptions { EdgeMode = EdgeMode.Aliased });

            context.FillRectangle(Resource("BackgroundPrimaryBrush"), new Rect(_renderCacheSize));

            Rect bounds = new Rect(OuterPadding, OuterPadding, Math.Max(0, width - OuterPadding * 2), Math.Max(0, height - OuterPadding * 2));

            if (bounds.Width <= 1 || bounds.Height <= 1)
            {
                return;
            }

            TreemapNode rootNode = BuildTreemapTree();

            if (rootNode == null)
            {
                return;
            }

            // The hover border is drawn over the cache, not into it.
            HitArea previousHover = _hoverHitArea;
            _hoverHitArea = null;
            List<TreemapNode> rootChildren = PrepareChildrenForLayout(rootNode, bounds);

            if (rootChildren.Count == 0)
            {
                DrawNode(context, rootNode, bounds, 0, ChartColors.GetFamilyColor(0), false, false, false);
            }
            else
            {
                DrawLayoutItems(context, rootNode, CreateSquarifiedLayout(rootChildren, bounds), 0, ChartColors.GetFamilyColor(0), false);
            }

            _hoverHitArea = previousHover;
        }

        private void DrawHoverOverlay(DrawingContext context)
        {
            if (_hoverHitArea == null || _renderCacheSize.Width <= 0 || _renderCacheSize.Height <= 0)
            {
                return;
            }

            double scaleX = Bounds.Width / _renderCacheSize.Width;
            double scaleY = Bounds.Height / _renderCacheSize.Height;
            Rect bounds = _hoverHitArea.Bounds;

            context.DrawRectangle(
                null,
                new Pen(Brushes.White, 2),
                new Rect(bounds.X * scaleX + 1, bounds.Y * scaleY + 1, Math.Max(0, bounds.Width * scaleX - 3), Math.Max(0, bounds.Height * scaleY - 3)));
        }

        private void DrawLayoutItems(DrawingContext context, TreemapNode parentNode, List<LayoutItem> layout, int depth, Color inheritedFamilyColor, bool familyLocked)
        {
            if (layout.Count == 0)
            {
                return;
            }

            bool createFamilies = !familyLocked && HasMeaningfulFamilySplit(parentNode, layout);
            int familyIndex = ChartColors.GetFamilyStartIndex(parentNode?.DisplayName);
            long parentSize = Math.Max(0, GetNodeSize(parentNode));

            foreach (LayoutItem item in layout)
            {
                Color childFamilyColor = inheritedFamilyColor;
                bool childFamilyLocked = familyLocked;
                bool isFamilyRoot = false;

                if (createFamilies && !item.Node.IsAggregate && !ShouldPromoteDescendantFamilies(parentNode, item.Node))
                {
                    childFamilyColor = ChartColors.GetFamilyColor(familyIndex);
                    familyIndex++;
                    childFamilyLocked = true;
                    isFamilyRoot = true;
                }

                long itemSize = Math.Max(0, GetNodeSize(item.Node));
                bool showDirectoryHeader =
                    childFamilyLocked &&
                    !isFamilyRoot &&
                    !item.Node.IsAggregate &&
                    item.Node.Entry != null &&
                    item.Node.Entry.IsDirectory &&
                    item.Node.Children.Count > 0 &&
                    parentSize > 0 &&
                    itemSize / (double)parentSize < FamilyPromotionMinimumShare;

                DrawNode(context, item.Node, item.Bounds, depth, childFamilyColor, childFamilyLocked, isFamilyRoot, showDirectoryHeader);
            }
        }

        private void DrawNode(
            DrawingContext context,
            TreemapNode node,
            Rect bounds,
            int depth,
            Color familyColor,
            bool familyLocked,
            bool isFamilyRoot,
            bool showDirectoryHeader)
        {
            if (bounds.Width < 1 || bounds.Height < 1)
            {
                return;
            }

            Rect hitBounds = Round(bounds);

            if (hitBounds.Width > 0 && hitBounds.Height > 0)
            {
                _hitAreas.Add(new HitArea(hitBounds, node));
            }

            Color nodeColor = node.IsAggregate
                ? GetAggregateColor(familyColor)
                : ChartColors.GetFamilyShade(familyColor, node.DisplayName, depth);
            Color gradientTop = ChartColors.Lighten(nodeColor, node.IsAggregate ? 1.05D : ChartColors.GradientTopFactor);
            Color gradientBottom = ChartColors.Darken(nodeColor, node.IsAggregate ? 0.94D : ChartColors.GradientBottomFactor);
            Rect pixelBounds = Snap(bounds);

            context.FillRectangle(VerticalGradient(gradientTop, gradientBottom, bounds), pixelBounds);

            bool isFile = !node.IsAggregate && (node.Entry == null || !node.Entry.IsDirectory);

            // Glossy top edge on files.
            if (isFile && bounds.Width >= 18 && bounds.Height >= 14)
            {
                Rect highlight = new Rect(
                    bounds.X + 1,
                    bounds.Y + 1,
                    Math.Max(0, bounds.Width - 2),
                    Math.Min(Math.Max(0, bounds.Height - 2), Math.Max(6, bounds.Height * 0.28)));
                context.FillRectangle(
                    VerticalGradient(Color.FromArgb(48, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), highlight),
                    Snap(highlight));
            }

            List<TreemapNode> visibleChildren = PrepareChildrenForLayout(node, bounds);
            bool canDrawChildren =
                !node.IsAggregate &&
                node.Entry != null &&
                node.Entry.IsDirectory &&
                visibleChildren.Count > 0 &&
                bounds.Width >= MinimumRecursiveWidth &&
                bounds.Height >= MinimumRecursiveHeight;
            bool canDrawDirectoryHeader = showDirectoryHeader && CanDrawDirectoryHeader(node, bounds);

            if (canDrawChildren)
            {
                double headerHeight =
                    isFamilyRoot && bounds.Width >= 60 && bounds.Height >= 44
                        ? Math.Min(FamilyHeaderHeight, Math.Max(0, bounds.Height - MinimumRecursiveHeight))
                        : canDrawDirectoryHeader ? FamilyHeaderHeight : 0;
                Rect contentBounds = new Rect(bounds.X, bounds.Y + headerHeight, bounds.Width, Math.Max(0, bounds.Height - headerHeight));

                DrawLayoutItems(context, node, CreateSquarifiedLayout(visibleChildren, contentBounds), depth + 1, familyColor, familyLocked);
            }
            else
            {
                DrawNodeLabel(context, node, bounds);
            }

            if (node.IsAggregate || node.Entry == null || !node.Entry.IsDirectory || isFamilyRoot)
            {
                DrawNodeBorder(context, node, bounds, familyColor, nodeColor, isFamilyRoot);
            }

            if (isFamilyRoot)
            {
                DrawFamilyCaption(context, node, bounds, familyColor);
            }
            else if (canDrawDirectoryHeader && canDrawChildren)
            {
                DrawDirectoryOverlayLabel(context, node, bounds, nodeColor);
            }
        }

        // Name and size of a tile with no children drawn, each on a dark
        // translucent band.
        private void DrawNodeLabel(DrawingContext context, TreemapNode node, Rect bounds)
        {
            long nodeSize = GetNodeSize(node);

            if (nodeSize < MinimumLabelSizeBytes || bounds.Height < MinimumLabelHeight)
            {
                return;
            }

            Rect labelBounds = Round(new Rect(bounds.X + 4, bounds.Y + 3, Math.Max(0, bounds.Width - 8), Math.Max(0, bounds.Height - 6)));

            if (labelBounds.Width <= 0 || labelBounds.Height <= 0)
            {
                return;
            }

            const double horizontalPadding = 3;
            string sizeText = SizeFormatter.Format(nodeSize);
            double requiredSizeWidth = MeasureUnpadded(sizeText) + horizontalPadding * 2;

            if (requiredSizeWidth > labelBounds.Width)
            {
                return;
            }

            IBrush bandBrush = new SolidColorBrush(Color.FromArgb(ChartColors.SegmentLabelBackgroundAlpha, 0, 0, 0));
            bool canDrawTwoLines = labelBounds.Height >= LineHeight * 2 && bounds.Width >= MinimumLabelWidth;
            double sizeTop = canDrawTwoLines ? labelBounds.Y + LineHeight : labelBounds.Y;

            if (canDrawTwoLines)
            {
                double titleWidth = Math.Min(labelBounds.Width, Math.Min(labelBounds.Width, MeasureUnpadded(node.DisplayName)) + horizontalPadding * 2);
                Rect titleBand = new Rect(labelBounds.X, labelBounds.Y, titleWidth, LineHeight);
                context.FillRectangle(bandBrush, titleBand);
                DrawTextLine(context, node.DisplayName, titleBand.Deflate(new Thickness(horizontalPadding, 0)), Brushes.White, padded: false);
            }

            Rect sizeBand = new Rect(labelBounds.X, sizeTop, requiredSizeWidth, LineHeight);
            context.FillRectangle(bandBrush, sizeBand);
            DrawTextLine(context, sizeText, sizeBand.Deflate(new Thickness(horizontalPadding, 0)), Brushes.White, padded: false);
        }

        private bool CanDrawDirectoryHeader(TreemapNode node, Rect bounds)
        {
            return node.Entry != null &&
                node.Entry.IsDirectory &&
                GetNodeSize(node) >= MinimumLabelSizeBytes &&
                bounds.Height >= FamilyHeaderHeight + MinimumRecursiveHeight &&
                bounds.Width >= MeasureUnpadded(SizeFormatter.Format(GetNodeSize(node))) + 8;
        }

        // Header of a large folder inside a family: name and size on a band
        // in a darker shade of the folder's color.
        private void DrawDirectoryOverlayLabel(DrawingContext context, TreemapNode node, Rect bounds, Color nodeColor)
        {
            const double horizontalPadding = 4;
            Rect header = Round(new Rect(bounds.X, bounds.Y, bounds.Width, FamilyHeaderHeight));
            Color back = ChartColors.Darken(nodeColor, 0.72D);

            context.FillRectangle(new SolidColorBrush(Color.FromArgb(245, back.R, back.G, back.B)), header);
            DrawHorizontalLine(context, new SolidColorBrush(WithAlpha(ChartColors.Lighten(nodeColor, 1.28D), 170)), header.Left, header.Right - 1, header.Bottom - 1);

            double textWidth = Math.Max(0, header.Width - horizontalPadding * 2);
            DrawTextLine(context, node.DisplayName, new Rect(header.X + horizontalPadding, header.Y, textWidth, LineHeight), Brushes.White, padded: false);
            DrawTextLine(context, SizeFormatter.Format(GetNodeSize(node)), new Rect(header.X + horizontalPadding, header.Y + LineHeight, textWidth, LineHeight), Brushes.White, padded: false);
        }

        // Header of a family's top folder: name and size on a dark band with
        // a line in the family color.
        private void DrawFamilyCaption(DrawingContext context, TreemapNode node, Rect bounds, Color familyColor)
        {
            if (node.Entry == null || !node.Entry.IsDirectory || bounds.Width < 60 || bounds.Height < 44)
            {
                return;
            }

            double headerHeight = Math.Round(Math.Min(FamilyHeaderHeight, Math.Max(0, bounds.Height - MinimumRecursiveHeight)));

            if (headerHeight <= 0)
            {
                return;
            }

            Rect header = new Rect(Math.Round(bounds.X), Math.Round(bounds.Y), Math.Max(0, Math.Round(bounds.Width)), headerHeight);
            context.FillRectangle(new SolidColorBrush(Color.FromArgb(110, 0, 0, 0)), header);
            DrawHorizontalLine(context, new SolidColorBrush(ChartColors.Lighten(familyColor, 1.18D)), header.Left, header.Right - 1, header.Bottom - 1);

            double textWidth = Math.Max(0, header.Width - 8);
            DrawTextLine(context, node.Entry.Name, new Rect(header.X + 4, header.Y, textWidth, Math.Min(LineHeight, header.Height)), Brushes.White, padded: false);

            if (header.Height > LineHeight)
            {
                DrawTextLine(
                    context,
                    SizeFormatter.Format(node.Entry.SizeBytes),
                    new Rect(header.X + 4, header.Y + LineHeight, textWidth, Math.Min(LineHeight, header.Height - LineHeight)),
                    Brushes.White,
                    padded: false);
            }
        }

        private void DrawNodeBorder(DrawingContext context, TreemapNode node, Rect bounds, Color familyColor, Color nodeColor, bool isFamilyRoot)
        {
            bool isLeafNode = !node.IsAggregate && (node.Entry == null || !node.Entry.IsDirectory);
            IBrush borderBrush = isFamilyRoot
                ? new SolidColorBrush(ChartColors.Lighten(familyColor, 1.35D))
                : isLeafNode
                    ? new SolidColorBrush(ChartColors.Darken(nodeColor, 0.58D))
                    : Resource("BorderBrush");
            Rect pixel = Snap(bounds);

            // GDI+ DrawRectangle(x, y, w - 1, h - 1): a 1 px outline on the
            // tile's outermost pixels. The 1.4 px family pen rasterizes the
            // same without anti-aliasing.
            context.DrawRectangle(null, new Pen(borderBrush, 1), new Rect(pixel.X + 0.5, pixel.Y + 0.5, Math.Max(0, pixel.Width - 1), Math.Max(0, pixel.Height - 1)));

            if (isLeafNode && bounds.Width >= 8 && bounds.Height >= 8)
            {
                IBrush highlight = new SolidColorBrush(WithAlpha(ChartColors.Lighten(nodeColor, 1.65D), 92));
                IBrush shadow = new SolidColorBrush(WithAlpha(ChartColors.Darken(nodeColor, 0.42D), 70));
                double left = pixel.X + 1;
                double top = pixel.Y + 1;
                double right = Math.Max(left, pixel.Right - 2);
                double bottom = Math.Max(top, pixel.Bottom - 2);

                DrawHorizontalLine(context, highlight, left, right, top);
                DrawVerticalLine(context, highlight, left, top, bottom);
                DrawHorizontalLine(context, shadow, left, right, bottom);
                DrawVerticalLine(context, shadow, right, top, bottom);
            }
        }

        // One pixel row from left to right (inclusive), as GDI+ DrawLine.
        private static void DrawHorizontalLine(DrawingContext context, IBrush brush, double left, double right, double y)
        {
            context.FillRectangle(brush, new Rect(left, y, Math.Max(1, right - left + 1), 1));
        }

        private static void DrawVerticalLine(DrawingContext context, IBrush brush, double x, double top, double bottom)
        {
            context.FillRectangle(brush, new Rect(x, top, 1, Math.Max(1, bottom - top + 1)));
        }

        private static IBrush VerticalGradient(Color top, Color bottom, Rect bounds)
        {
            return new LinearGradientBrush
            {
                StartPoint = new RelativePoint(bounds.TopLeft, RelativeUnit.Absolute),
                EndPoint = new RelativePoint(bounds.BottomLeft, RelativeUnit.Absolute),
                GradientStops = { new GradientStop(top, 0), new GradientStop(bottom, 1) },
            };
        }

        private double MeasureUnpadded(string text)
        {
            return Math.Ceiling(CreateText(text, null).WidthIncludingTrailingWhitespace);
        }

        private static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

        private static Color GetAggregateColor(Color familyColor)
        {
            static byte Scale(byte value) => (byte)Math.Max(24, Math.Min(235, (int)Math.Round(value * 0.78D)));
            return Color.FromRgb(Scale(familyColor.R), Scale(familyColor.G), Scale(familyColor.B));
        }

        // Rectangle.Round: each coordinate and size rounded on its own.
        private static Rect Round(Rect bounds)
        {
            return new Rect(Math.Round(bounds.X), Math.Round(bounds.Y), Math.Round(bounds.Width), Math.Round(bounds.Height));
        }

        // The pixels GDI+ fills for a float rectangle without anti-aliasing.
        private static Rect Snap(Rect bounds)
        {
            double left = Math.Round(bounds.Left);
            double top = Math.Round(bounds.Top);
            return new Rect(left, top, Math.Max(0, Math.Round(bounds.Right) - left), Math.Max(0, Math.Round(bounds.Bottom) - top));
        }

        // ----- tree -------------------------------------------------------

        private TreemapNode BuildTreemapTree()
        {
            if (_entry == null)
            {
                return null;
            }

            if (_cachedRootNode != null)
            {
                return _cachedRootNode;
            }

            Dictionary<string, TreemapNode> directoriesByPath = new Dictionary<string, TreemapNode>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> includedFilePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            TreemapNode rootNode = BuildDirectoryTree(_entry, directoriesByPath, includedFilePaths);

            if (_entry.IsDirectory)
            {
                // Files the scan has listed but not yet attached to a folder.
                FileSystemEntry fileSourceRoot = _rootEntry ?? _entry;
                List<FileSystemEntry> allFiles;

                lock (fileSourceRoot.AllFiles)
                {
                    allFiles = new List<FileSystemEntry>(fileSourceRoot.AllFiles);
                }

                foreach (FileSystemEntry file in allFiles)
                {
                    if (file == null || file.IsDirectory || string.IsNullOrWhiteSpace(file.FullPath) ||
                        !EntryTreeCanvas.IsSameOrDescendantPath(file.FullPath, _entry.FullPath) ||
                        !includedFilePaths.Add(NormalizePath(file.FullPath)))
                    {
                        continue;
                    }

                    string parentPath = Path.GetDirectoryName(file.FullPath);

                    if (!string.IsNullOrWhiteSpace(parentPath) &&
                        directoriesByPath.TryGetValue(NormalizePath(parentPath), out TreemapNode parentNode))
                    {
                        parentNode.Children.Add(new TreemapNode(file));
                    }
                }
            }

            SortChildren(rootNode);
            _cachedRootNode = rootNode;
            return rootNode;
        }

        private static TreemapNode BuildDirectoryTree(FileSystemEntry entry, Dictionary<string, TreemapNode> directoriesByPath, HashSet<string> includedFilePaths)
        {
            TreemapNode node = new TreemapNode(entry);

            if (!entry.IsDirectory)
            {
                if (!string.IsNullOrWhiteSpace(entry.FullPath))
                {
                    includedFilePaths.Add(NormalizePath(entry.FullPath));
                }

                return node;
            }

            if (!string.IsNullOrWhiteSpace(entry.FullPath))
            {
                directoriesByPath[NormalizePath(entry.FullPath)] = node;
            }

            List<FileSystemEntry> children;

            lock (entry.Children)
            {
                children = new List<FileSystemEntry>(entry.Children);
            }

            foreach (FileSystemEntry child in children.Where(child => child != null))
            {
                node.Children.Add(BuildDirectoryTree(child, directoriesByPath, includedFilePaths));
            }

            return node;
        }

        private static void SortChildren(TreemapNode node)
        {
            node.Children.Sort((left, right) =>
            {
                int sizeCompare = GetNodeSize(right).CompareTo(GetNodeSize(left));
                return sizeCompare != 0
                    ? sizeCompare
                    : string.Compare(left.DisplayName, right.DisplayName, StringComparison.CurrentCultureIgnoreCase);
            });

            foreach (TreemapNode child in node.Children)
            {
                SortChildren(child);
            }
        }

        private static string NormalizePath(string path)
        {
            string trimmed = path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return trimmed.Length == 0 ? path.Trim() : trimmed;
        }

        // ----- families ---------------------------------------------------

        private static bool ShouldPromoteDescendantFamilies(TreemapNode parentNode, TreemapNode childNode)
        {
            if (parentNode == null || childNode.IsAggregate || childNode.Entry == null || !childNode.Entry.IsDirectory)
            {
                return false;
            }

            long parentSize = GetNodeSize(parentNode);
            long childSize = GetNodeSize(childNode);

            return parentSize > 0 &&
                childSize > 0 &&
                childSize / (double)parentSize >= FamilyPromotionMinimumShare &&
                HasDescendantMeaningfulFamilySplit(childNode, 0);
        }

        private static bool HasDescendantMeaningfulFamilySplit(TreemapNode node, int depth)
        {
            long nodeSize = GetNodeSize(node);

            if (node.Children.Count == 0 || depth >= 8 || nodeSize <= 0)
            {
                return false;
            }

            if (node.Children.Count(child => !child.IsAggregate && GetNodeSize(child) / (double)nodeSize >= FamilySplitMinimumShare) >= 2)
            {
                return true;
            }

            TreemapNode dominantChild = node.Children
                .Where(child => !child.IsAggregate && child.Entry != null && child.Entry.IsDirectory)
                .OrderByDescending(GetNodeSize)
                .FirstOrDefault();

            return dominantChild != null &&
                GetNodeSize(dominantChild) / (double)nodeSize >= 0.60D &&
                HasDescendantMeaningfulFamilySplit(dominantChild, depth + 1);
        }

        private static bool HasMeaningfulFamilySplit(TreemapNode parentNode, List<LayoutItem> layout)
        {
            long parentSize = GetNodeSize(parentNode);

            return parentNode != null &&
                parentSize > 0 &&
                layout.Count(item => !item.Node.IsAggregate && GetNodeSize(item.Node) / (double)parentSize >= FamilySplitMinimumShare) >= 2;
        }

        // ----- layout (pure) ----------------------------------------------

        // The children worth a tile: up to 160, each with at least 30 px²
        // (the 8 largest always); the rest becomes one "Other (n)" tile.
        internal static List<TreemapNode> PrepareChildrenForLayout(TreemapNode node, Rect bounds)
        {
            List<TreemapNode> result = new List<TreemapNode>();

            if (node == null || node.Children.Count == 0 || bounds.Width <= 0 || bounds.Height <= 0)
            {
                return result;
            }

            long totalSize = node.Children.Sum(child => Math.Max(0, GetNodeSize(child)));

            if (totalSize <= 0)
            {
                return result;
            }

            double availableArea = bounds.Width * bounds.Height;
            long aggregateSize = 0;
            int aggregateCount = 0;

            foreach (TreemapNode child in node.Children.OrderByDescending(GetNodeSize))
            {
                long childSize = Math.Max(0, GetNodeSize(child));

                if (childSize <= 0)
                {
                    continue;
                }

                double estimatedArea = availableArea * childSize / totalSize;

                if (result.Count < MaximumVisibleChildren && (estimatedArea >= MinimumChildPixelArea || result.Count < 8))
                {
                    result.Add(child);
                }
                else
                {
                    aggregateSize += childSize;
                    aggregateCount += child.IsAggregate ? child.AggregateCount : 1;
                }
            }

            if (aggregateSize > 0)
            {
                result.Add(TreemapNode.CreateAggregate(aggregateSize, aggregateCount, node.Entry));
            }

            return result;
        }

        internal static long GetNodeSize(TreemapNode node)
        {
            if (node == null)
            {
                return 0;
            }

            if (node.ExplicitSizeBytes > 0)
            {
                return node.ExplicitSizeBytes;
            }

            long entrySize = Math.Max(0, node.Entry?.SizeBytes ?? 0);
            return entrySize > 0 ? entrySize : node.Children.Sum(child => Math.Max(0, GetNodeSize(child)));
        }

        // Squarified treemap (Bruls, Huizing, van Wijk), in float like GDI+'s
        // RectangleF so tiles land on the same pixels as in WinForms.
        internal static List<LayoutItem> CreateSquarifiedLayout(List<TreemapNode> nodes, Rect bounds)
        {
            List<LayoutItem> result = new List<LayoutItem>();

            if (nodes.Count == 0 || bounds.Width <= 0 || bounds.Height <= 0)
            {
                return result;
            }

            List<AreaItem> items = nodes
                .Where(node => GetNodeSize(node) > 0)
                .Select(node => new AreaItem(node, GetNodeSize(node)))
                .OrderByDescending(item => item.Size)
                .ToList();

            if (items.Count == 0)
            {
                return result;
            }

            double totalSize = items.Sum(item => (double)item.Size);
            double totalArea = (float)bounds.Width * (float)bounds.Height;

            foreach (AreaItem item in items)
            {
                item.Area = totalArea * item.Size / totalSize;
            }

            FloatRect remaining = new FloatRect((float)bounds.X, (float)bounds.Y, (float)bounds.Width, (float)bounds.Height);
            List<AreaItem> row = new List<AreaItem>();
            int index = 0;

            while (index < items.Count)
            {
                AreaItem candidate = items[index];
                float shortSide = Math.Max(1F, Math.Min(remaining.Width, remaining.Height));

                if (row.Count == 0 || WorstAspectRatio(row, shortSide) >= WorstAspectRatio(new List<AreaItem>(row) { candidate }, shortSide))
                {
                    row.Add(candidate);
                    index++;
                    continue;
                }

                LayoutRow(row, ref remaining, result);
                row.Clear();
            }

            if (row.Count > 0)
            {
                LayoutRow(row, ref remaining, result);
            }

            return result;
        }

        private static double WorstAspectRatio(List<AreaItem> row, float shortSide)
        {
            double sum = row.Sum(item => item.Area);
            double min = row.Min(item => item.Area);

            if (sum <= 0 || min <= 0)
            {
                return double.MaxValue;
            }

            double sideSquared = shortSide * shortSide;
            double sumSquared = sum * sum;
            return Math.Max(sideSquared * row.Max(item => item.Area) / sumSquared, sumSquared / (sideSquared * min));
        }

        private static void LayoutRow(List<AreaItem> row, ref FloatRect remaining, List<LayoutItem> result)
        {
            if (remaining.Width <= 0F || remaining.Height <= 0F)
            {
                return;
            }

            double rowArea = row.Sum(item => item.Area);

            if (remaining.Width >= remaining.Height)
            {
                float rowWidth = (float)Math.Min(remaining.Width, rowArea / Math.Max(1F, remaining.Height));
                float y = remaining.Y;

                for (int index = 0; index < row.Count; index++)
                {
                    float height = index == row.Count - 1
                        ? remaining.Bottom - y
                        : (float)(row[index].Area / Math.Max(1F, rowWidth));
                    result.Add(new LayoutItem(row[index].Node, new Rect(remaining.X, y, Math.Max(0F, rowWidth), Math.Max(0F, height))));
                    y += height;
                }

                remaining = new FloatRect(remaining.X + rowWidth, remaining.Y, Math.Max(0F, remaining.Width - rowWidth), remaining.Height);
            }
            else
            {
                float rowHeight = (float)Math.Min(remaining.Height, rowArea / Math.Max(1F, remaining.Width));
                float x = remaining.X;

                for (int index = 0; index < row.Count; index++)
                {
                    float width = index == row.Count - 1
                        ? remaining.Right - x
                        : (float)(row[index].Area / Math.Max(1F, rowHeight));
                    result.Add(new LayoutItem(row[index].Node, new Rect(x, remaining.Y, Math.Max(0F, width), Math.Max(0F, rowHeight))));
                    x += width;
                }

                remaining = new FloatRect(remaining.X, remaining.Y + rowHeight, remaining.Width, Math.Max(0F, remaining.Height - rowHeight));
            }
        }

        private readonly struct FloatRect
        {
            public FloatRect(float x, float y, float width, float height)
            {
                X = x;
                Y = y;
                Width = width;
                Height = height;
            }

            public float X { get; }
            public float Y { get; }
            public float Width { get; }
            public float Height { get; }
            public float Right => X + Width;
            public float Bottom => Y + Height;
        }

        internal sealed class TreemapNode
        {
            public TreemapNode(FileSystemEntry entry)
            {
                Entry = entry;
                DisplayName = entry?.Name ?? string.Empty;
            }

            private TreemapNode(string displayName, long explicitSizeBytes, int aggregateCount, FileSystemEntry aggregateParentEntry)
            {
                DisplayName = displayName;
                ExplicitSizeBytes = explicitSizeBytes;
                AggregateCount = aggregateCount;
                AggregateParentEntry = aggregateParentEntry;
                IsAggregate = true;
            }

            public FileSystemEntry Entry { get; }
            public FileSystemEntry AggregateParentEntry { get; }
            public string DisplayName { get; }
            public long ExplicitSizeBytes { get; }
            public int AggregateCount { get; }
            public bool IsAggregate { get; }
            public List<TreemapNode> Children { get; } = new List<TreemapNode>();

            // Not localized in WinForms either.
            public static TreemapNode CreateAggregate(long sizeBytes, int count, FileSystemEntry aggregateParentEntry)
            {
                return new TreemapNode("Other (" + count + ")", sizeBytes, count, aggregateParentEntry);
            }
        }

        private sealed class AreaItem
        {
            public AreaItem(TreemapNode node, long size)
            {
                Node = node;
                Size = size;
            }

            public TreemapNode Node { get; }
            public long Size { get; }
            public double Area { get; set; }
        }

        internal sealed class LayoutItem
        {
            public LayoutItem(TreemapNode node, Rect bounds)
            {
                Node = node;
                Bounds = bounds;
            }

            public TreemapNode Node { get; }
            public Rect Bounds { get; }
        }

        private sealed class HitArea
        {
            public HitArea(Rect bounds, TreemapNode node)
            {
                Bounds = bounds;
                Node = node;
            }

            public Rect Bounds { get; }
            public TreemapNode Node { get; }
        }
    }
}
