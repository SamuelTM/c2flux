// last comment update 2026-08-21, 09:12
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace c2flux
{
    public sealed class AdvancedFeaturesForm : Form
    {
        // All visual design, colors, spacing and sizing must come from AntdThemeService.
        // All visible UI text must use LocalizationService.
        private sealed class FileTypeRow
        {
            public string Extension { get; set; }
            public double UsagePercent { get; set; }
            public string SizeGb { get; set; }
            public string SizeMb { get; set; }
            public long SizeBytes { get; set; }
        }

        private sealed class FileTypeCategoryRow
        {
            public string FileType { get; set; }
            public double UsagePercent { get; set; }
            public string SizeGb { get; set; }
            public string SizeMb { get; set; }
            public long SizeBytes { get; set; }
        }

        private sealed class LargestFileRow
        {
            public string Name { get; set; }
            public double UsagePercent { get; set; }
            public string FormattedSize { get; set; }
            public long SizeBytes { get; set; }
            public DateTime LastWriteTime { get; set; }
            public string FullPath { get; set; }
        }

        private sealed class RedundancyRow
        {
            public string Name { get; set; }
            public double? UsagePercent { get; set; }
            public int? Count { get; set; }
            public long? SizeBytes { get; set; }
            public long? TotalSizeBytes { get; set; }
            public bool IsLocation { get; set; }
            public List<RedundancyRow> Children { get; set; }
        }

        private sealed class RedundancyAnalysisProgress :
            IProgress<RedundancyAnalysisGroup>
        {
            private readonly ConcurrentQueue<RedundancyAnalysisGroup>
                _pendingGroups;

            public RedundancyAnalysisProgress(
                ConcurrentQueue<RedundancyAnalysisGroup> pendingGroups)
            {
                _pendingGroups = pendingGroups ??
                    throw new ArgumentNullException(nameof(pendingGroups));
            }

            public void Report(
                RedundancyAnalysisGroup value)
            {
                if (value != null)
                {
                    _pendingGroups.Enqueue(value);
                }
            }
        }

        private enum SizeUnit
        {
            Bytes,
            KB,
            MB,
            GB,
            TB
        }

        // Shared Analysis table behavior; visual styling must remain centralized in AntdThemeService.
        private class Analysis_ResponsiveTableGrid : AntdUI.Table
        {
            public Analysis_ResponsiveTableGrid()
            {
                Dock = DockStyle.Fill;
                FixedHeader = true;
                VisibleHeader = true;
                EnableHeaderResizing = true;
                ColumnDragSort = false;
                MultipleRows = false;
                LostFocusClearSelection = false;
                MouseClickPenetration = true;
                ScrollBarAvoidHeader = true;
                AutoSizeColumnsMode = AntdUI.ColumnsMode.Fill;
                ShowTip = true;
                EmptyHeader = true;
                EmptyText = string.Empty;
    
                ApplyAntdUIStyle();
            }
    
            public void SetResponsiveColumns(
                params (string ColumnName, int Percentage)[] responsiveColumns)
            {
                if (responsiveColumns == null || Columns == null)
                    return;
    
                foreach ((string ColumnName, int Percentage) definition in responsiveColumns)
                {
                    AntdUI.Column column = Columns.FirstOrDefault(
                        currentColumn => string.Equals(
                            currentColumn.Key,
                            definition.ColumnName,
                            StringComparison.Ordinal));
    
                    if (column == null)
                        continue;
    
                    column.Width = $"{Math.Max(0, definition.Percentage)}%";
                }
    
                LoadLayout();
                Invalidate();
            }
    
            public void ApplyAntdUIStyle()
            {
                AntdThemeService.ConfigureAnalysisTable(this);
                LoadLayout();
                Invalidate();
            }
        }

        private readonly FileSystemEntry _rootEntry;
        private readonly Analysis_ResponsiveTableGrid _fileTypeGrid =
            new Analysis_ResponsiveTableGrid();
        private readonly Analysis_ResponsiveTableGrid _fileTypeCategoryGrid =
            new Analysis_ResponsiveTableGrid();
        private readonly Analysis_ResponsiveTableGrid _largestFilesGrid =
            new Analysis_ResponsiveTableGrid();
        private readonly Analysis_ResponsiveTableGrid _redundancyGrid =
            new Analysis_ResponsiveTableGrid();
        private readonly AntdUI.Progress _redundancyAnalysisProgressBar =
            new AntdUI.Progress();
        private readonly CancellationTokenSource _redundancyCancellationTokenSource =
            new CancellationTokenSource();
        private readonly ContextMenuStrip _largestFilesContextMenu =
            new ContextMenuStrip();
        private readonly ContextMenuStrip _redundancyContextMenu =
            new ContextMenuStrip();
        private LargestFileRow _largestFilesContextRow;
        private RedundancyRow _redundancyContextRow;
        private List<FileTypeRow> _fileTypeRows =
            new List<FileTypeRow>();
        private List<FileTypeCategoryRow> _fileTypeCategoryRows =
            new List<FileTypeCategoryRow>();
        private List<LargestFileRow> _largestFileRows =
            new List<LargestFileRow>();
        private AntdUI.AntList<RedundancyRow> _redundancyRows =
            new AntdUI.AntList<RedundancyRow>();
        private readonly HashSet<RedundancyRow> _expandedRedundancyRows =
            new HashSet<RedundancyRow>();
        private string _redundancyAnalysisProgressText =
            string.Empty;
        private RedundancyAnalysisPhase _displayedRedundancyProgressPhase =
            RedundancyAnalysisPhase.SizeGrouping;
        private RedundancyAnalysisPhase _pendingRedundancyProgressPhase =
            RedundancyAnalysisPhase.SizeGrouping;
        private DateTime _pendingRedundancyProgressPhaseSince =
            DateTime.MinValue;
        private AntdUI.TabPage _redundanciesPage;
        private bool _redundanciesLoaded;
        private bool _redundanciesLoading;
        private SizeUnit _sizeUnit = SizeUnit.MB;

        public AdvancedFeaturesForm(
    FileSystemEntry rootEntry,
    AppSettings settings,
    Chart_TableGridChart entryGrid)
        {
            _rootEntry = rootEntry ??
                throw new ArgumentNullException(nameof(rootEntry));

            AntdThemeService.Apply(settings.Layout);

            Text = LocalizationService.GetText("Advanced.Title");
            Icon = AppResources.ApplicationIcon;
            Width = 1050;
            Height = 700;
            AutoSize = false;
            MinimumSize = Size.Empty;
            MaximumSize = Size.Empty;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = AntdThemeService.BackgroundPrimary;
            ForeColor = AntdThemeService.TextPrimary;

            AntdUI.Tabs tabs = new AntdUI.Tabs
            {
                Name = "analysisTabs",
                Dock = DockStyle.Fill
            };

            AntdThemeService.ConfigureAnalysisTabs(tabs);
            tabs.Pages.Add(CreateFileTypesPage());
            tabs.Pages.Add(CreateFileTypeCategoriesPage());
            tabs.Pages.Add(CreateLargestFilesPage());
            _redundanciesPage = CreateRedundanciesPage();
            tabs.Pages.Add(_redundanciesPage);
            tabs.SelectedIndexChanged += async (sender, e) =>
            {
                if (ReferenceEquals(
                        tabs.SelectedTab,
                        _redundanciesPage))
                {
                    
                    await LoadRedundanciesAsync();
                }
            };
            Controls.Add(tabs);

            AntdThemeService.Apply(this, settings.Layout);
            ApplyTheme();
            RefreshData();
        }

        private AntdUI.TabPage CreateFileTypesPage()
        {
            _fileTypeGrid.Columns = new AntdUI.ColumnCollection
            {
                new AntdUI.Column(
                    nameof(FileTypeRow.Extension),
                    LocalizationService.GetText("Advanced.FileType"))
                {
                    Ellipsis = false,
                    SortOrder = true,
                    Render = (value, record, rowIndex) =>
                    {
                        string text = record is FileTypeRow row
                            ? row.Extension
                            : value?.ToString();

                        return AntdThemeService.CreateVisibleTableCellText(
                            text);
                    }
                },
                new AntdUI.Column(
                    nameof(FileTypeRow.UsagePercent),
                    LocalizationService.GetText("Advanced.Usage"),
                    AntdUI.ColumnAlign.Center)
                {
                    Width =
                        (AntdThemeService.TableProgressWidth +
                         (AntdThemeService.TableCellHorizontalPadding * 2))
                        .ToString(),
                    SortOrder = true,
                    Render = (value, record, rowIndex) =>
                    {
                        double percent = record is FileTypeRow row
                            ? row.UsagePercent
                            : 0D;

                        return new AnalysisPercentCellProgress(
                            (float)Math.Clamp(
                                percent / 100D,
                                0D,
                                1D),
                            $"{percent:0.0} %");
                    }
                },
                new AntdUI.Column(
                    nameof(FileTypeRow.SizeGb),
                    LocalizationService.GetText("Advanced.SizeGb"),
                    AntdUI.ColumnAlign.Right)
                {
                    Width = "auto",
                    Ellipsis = true,
                    SortOrder = true
                },
                new AntdUI.Column(
                    nameof(FileTypeRow.SizeMb),
                    LocalizationService.GetText("Advanced.SizeMb"),
                    AntdUI.ColumnAlign.Right)
                {
                    Width = "auto",
                    Ellipsis = true,
                    SortOrder = true
                }
            };

            _fileTypeGrid.SetResponsiveColumns(
                (
                    nameof(FileTypeRow.Extension),
                    AntdThemeService.AnalysisFileTypeColumnWidthPercent
                ));

            return CreatePage(
                LocalizationService.GetText("Advanced.FileTypes"),
                _fileTypeGrid);
        }

        private AntdUI.TabPage CreateFileTypeCategoriesPage()
        {
            _fileTypeCategoryGrid.Columns =
                new AntdUI.ColumnCollection
                {
                    new AntdUI.Column(
                        nameof(FileTypeCategoryRow.FileType),
                        LocalizationService.GetText(
                            "Advanced.FileCategories"))
                    {
                        Ellipsis = false,
                        SortOrder = true,
                        Render = (value, record, rowIndex) =>
                        {
                            string text =
                                record is FileTypeCategoryRow row
                                    ? row.FileType
                                    : value?.ToString();

                            return AntdThemeService
                                .CreateVisibleTableCellText(text);
                        }
                    },
                    new AntdUI.Column(
                        nameof(FileTypeCategoryRow.UsagePercent),
                        LocalizationService.GetText(
                            "Advanced.Usage"),
                        AntdUI.ColumnAlign.Center)
                    {
                        Width =
                            (AntdThemeService.TableProgressWidth +
                             (AntdThemeService.TableCellHorizontalPadding * 2))
                            .ToString(),
                        SortOrder = true,
                        Render = (value, record, rowIndex) =>
                        {
                            double percent =
                                record is FileTypeCategoryRow row
                                    ? row.UsagePercent
                                    : 0D;

                            return new AnalysisPercentCellProgress(
                                (float)Math.Clamp(
                                    percent / 100D,
                                    0D,
                                    1D),
                                $"{percent:0.0} %");
                        }
                    },
                    new AntdUI.Column(
                        nameof(FileTypeCategoryRow.SizeGb),
                        LocalizationService.GetText(
                            "Advanced.SizeGb"),
                        AntdUI.ColumnAlign.Right)
                    {
                        Width = "auto",
                        Ellipsis = true,
                        SortOrder = true
                    },
                    new AntdUI.Column(
                        nameof(FileTypeCategoryRow.SizeMb),
                        LocalizationService.GetText(
                            "Advanced.SizeMb"),
                        AntdUI.ColumnAlign.Right)
                    {
                        Width = "auto",
                        Ellipsis = true,
                        SortOrder = true
                    }
                };

            _fileTypeCategoryGrid.SetResponsiveColumns(
                (
                    nameof(FileTypeCategoryRow.FileType),
                    AntdThemeService
                        .AnalysisFileTypeCategoryColumnWidthPercent
                ));

            return CreatePage(
                LocalizationService.GetText(
                    "Advanced.FileCategories"),
                _fileTypeCategoryGrid);
        }

        private AntdUI.TabPage CreateRedundanciesPage()
        {
            _redundancyGrid.Columns =
                new AntdUI.ColumnCollection
                {
                    new AntdUI.Column(
                        nameof(RedundancyRow.Name),
                        LocalizationService.GetText(
                            "Common.Name"))
                    {
                        KeyTree =
                            nameof(RedundancyRow.Children),
                        Ellipsis = true,
                        SortOrder = true,
                        Render = (value, record, rowIndex) =>
                        {
                            string name =
                                record is RedundancyRow row
                                    ? row.Name
                                    : value?.ToString();

                            return AntdThemeService
                                .CreateVisibleTableCellText(name);
                        }
                    },
                    new AntdUI.Column(
                        nameof(RedundancyRow.UsagePercent),
                        LocalizationService.GetText(
                            "Advanced.Usage"),
                        AntdUI.ColumnAlign.Center)
                    {
                        Width =
                            (AntdThemeService.TableProgressWidth +
                             (AntdThemeService.TableCellHorizontalPadding * 2))
                            .ToString(),
                        SortOrder = true,
                        Render = (value, record, rowIndex) =>
                        {
                            if (record is not RedundancyRow row ||
                                row.IsLocation ||
                                !row.UsagePercent.HasValue)
                            {
                                return string.Empty;
                            }

                            double percent =
                                row.UsagePercent.Value;

                            return new AnalysisPercentCellProgress(
                                (float)Math.Clamp(
                                    percent / 100D,
                                    0D,
                                    1D),
                                $"{percent:0.0} %");
                        }
                    },
                    new AntdUI.Column(
                        nameof(RedundancyRow.Count),
                        LocalizationService.GetText(
                            "Advanced.Count"),
                        AntdUI.ColumnAlign.Right)
                    {
                        Width = "auto",
                        Ellipsis = true,
                        SortOrder = true
                    },
                    new AntdUI.Column(
                        nameof(RedundancyRow.SizeBytes),
                        LocalizationService.GetText(
                            "Advanced.SizeGb"),
                        AntdUI.ColumnAlign.Right)
                    {
                        Width = "auto",
                        Ellipsis = true,
                        SortOrder = true,
                        Render = (value, record, rowIndex) =>
                        {
                            if (record is not RedundancyRow row ||
                                row.IsLocation ||
                                !row.SizeBytes.HasValue)
                            {
                                return string.Empty;
                            }

                            return
                                (row.SizeBytes.Value /
                                 (1024D * 1024D * 1024D))
                                .ToString("N2") +
                                " GB";
                        }
                    },
                    new AntdUI.Column(
                        nameof(RedundancyRow.TotalSizeBytes),
                        LocalizationService.GetText(
                            "ScanHistory.TotalSize") +
                            " (GB)",
                        AntdUI.ColumnAlign.Right)
                    {
                        Width = "auto",
                        Ellipsis = true,
                        SortOrder = true,
                        Render = (value, record, rowIndex) =>
                        {
                            if (record is not RedundancyRow row ||
                                row.IsLocation ||
                                !row.TotalSizeBytes.HasValue)
                            {
                                return string.Empty;
                            }

                            return
                                (row.TotalSizeBytes.Value /
                                 (1024D * 1024D * 1024D))
                                .ToString("N2") +
                                " GB";
                        }
                    }
                };

            _redundancyGrid.AutoSizeColumnsMode =
                AntdUI.ColumnsMode.Fill;
            _redundancyGrid.DefaultExpand = false;
            _redundancyGrid.TooltipConfig =
                new AntdUI.TooltipConfig
                {
                    CustomWidth =
                        Math.Max(
                            1,
                            Screen.FromControl(
                                _redundancyGrid)
                                .Bounds.Width / 4)
                };
            _redundancyGrid.CellHover +=
                RedundancyGrid_CellHover;
            _redundancyGrid.ExpandChanged +=
                RedundancyGrid_ExpandChanged;
            _redundancyGrid.MouseDown +=
                RedundancyGrid_MouseDown;
            _redundancyGrid.CellClickBegin +=
                RedundancyGrid_CellClickBegin;

            ToolStripMenuItem openParentFolderItem =
                new ToolStripMenuItem(
                    LocalizationService.GetText(
                        "Search.OpenParentFolder"));
            openParentFolderItem.Click +=
                RedundancyOpenParentFolder_Click;
            _redundancyContextMenu.Items.Add(
                openParentFolderItem);
            _redundancyContextMenu.Opening +=
                (sender, e) =>
                    e.Cancel =
                        _redundancyContextRow == null ||
                        !_redundancyContextRow.IsLocation;
            AntdThemeService.ConfigureContextMenu(
                _redundancyContextMenu);
            _redundancyGrid.ContextMenuStrip =
                _redundancyContextMenu;

            _redundancyGrid.SetResponsiveColumns(
                (
                    nameof(RedundancyRow.Name),
                    AntdThemeService
                        .AnalysisRedundancyNameColumnWidthPercent
                ));

            _redundancyAnalysisProgressBar.Dock =
                DockStyle.Top;
            _redundancyAnalysisProgressBar.Height = 28;
            _redundancyAnalysisProgressBar.Margin =
                Padding.Empty;
            _redundancyAnalysisProgressBar.Back =
                AntdThemeService.TableProgressBackColor;
            _redundancyAnalysisProgressBar.Fill =
                AntdThemeService.TableProgressFillColor;
            _redundancyAnalysisProgressBar.ForeColor =
                AntdThemeService.TextPrimary;
            _redundancyAnalysisProgressBar.Radius =
                AntdThemeService.TableProgressRadius;
            _redundancyAnalysisProgressBar.UseSystemText =
                false;
            _redundancyAnalysisProgressBar.UseTextCenter =
                true;
            _redundancyAnalysisProgressBar.ValueFormatChanged +=
                (sender, e) =>
                    _redundancyAnalysisProgressText;
            _redundancyAnalysisProgressBar.Value = 0F;
            _redundancyAnalysisProgressText =
                $"0 % ({LocalizationService.GetText("Advanced.Redundancy.Progress.SizeGrouping")})";

            TableLayoutPanel redundancyContent =
                new TableLayoutPanel
                {
                    BackColor =
                        AntdThemeService.BackgroundPrimary,
                    Padding = Padding.Empty,
                    Margin = Padding.Empty,
                    ColumnCount = 1,
                    RowCount = 2
                };

            redundancyContent.ColumnStyles.Add(
                new ColumnStyle(
                    SizeType.Percent,
                    100F));
            redundancyContent.RowStyles.Add(
                new RowStyle(
                    SizeType.Absolute,
                    _redundancyAnalysisProgressBar.Height));
            redundancyContent.RowStyles.Add(
                new RowStyle(
                    SizeType.Percent,
                    100F));

            _redundancyAnalysisProgressBar.Dock =
                DockStyle.Fill;
            _redundancyGrid.Dock =
                DockStyle.Fill;

            redundancyContent.Controls.Add(
                _redundancyAnalysisProgressBar,
                0,
                0);
            redundancyContent.Controls.Add(
                _redundancyGrid,
                0,
                1);

            return CreatePage(
                LocalizationService.GetText(
                    "Advanced.Redundancies"),
                redundancyContent);
        }

        // Redundancy analysis is lazy-loaded once and updates results progressively.
        private async Task LoadRedundanciesAsync()
        {
            if (_redundanciesLoaded ||
                _redundanciesLoading)
            {
                return;
            }

            _redundanciesLoading = true;

            ConcurrentQueue<RedundancyAnalysisGroup>
                pendingGroups =
                    new ConcurrentQueue<RedundancyAnalysisGroup>();

            using System.Windows.Forms.Timer updateTimer =
                new System.Windows.Forms.Timer
                {
                    Interval = 200
                };

            int previousAnimationTime =
                _redundancyGrid.AnimationTime;

            try
            {
                List<FileSystemEntry> files =
                    GetFiles();

                long totalBytes =
                    files.Sum(file => file.SizeBytes);

                _redundancyRows =
                    new AntdUI.AntList<RedundancyRow>();
                _expandedRedundancyRows.Clear();
                _redundancyGrid.Binding(
                    _redundancyRows);

                _redundancyGrid.AnimationTime = 0;

                RedundancyAnalysisProgress progress =
                    new RedundancyAnalysisProgress(
                        pendingGroups);

                _displayedRedundancyProgressPhase =
                    RedundancyAnalysisPhase.SizeGrouping;
                _pendingRedundancyProgressPhase =
                    RedundancyAnalysisPhase.SizeGrouping;
                _pendingRedundancyProgressPhaseSince =
                    DateTime.UtcNow;
                _redundancyAnalysisProgressText =
                    $"0 % ({LocalizationService.GetText("Advanced.Redundancy.Progress.SizeGrouping")})";
                _redundancyAnalysisProgressBar.Value = 0F;

                Progress<RedundancyAnalysisProgressInfo>
                    analysisProgress =
                        new Progress<RedundancyAnalysisProgressInfo>(
                            progressInfo =>
                            {
                                if (IsDisposed ||
                                    Disposing ||
                                    progressInfo == null)
                                {
                                    return;
                                }

                                int clampedPercentage =
                                    Math.Clamp(
                                        progressInfo.Percentage,
                                        0,
                                        100);

                                UpdateRedundancyProgressPhase(
                                    progressInfo.Phase);

                                _redundancyAnalysisProgressText =
                                    $"{clampedPercentage} % ({GetRedundancyProgressAction(_displayedRedundancyProgressPhase)})";
                                _redundancyAnalysisProgressBar.Value =
                                    clampedPercentage / 100F;
                                _redundancyAnalysisProgressBar.Invalidate();
                            });

                updateTimer.Tick +=
                    (sender, e) =>
                        FlushPendingRedundancyGroups(
                            pendingGroups,
                            totalBytes,
                            100);

                updateTimer.Start();

                await Task.Run(
                    () =>
                        RedundancyAnalysisService.Analyze(
                            files,
                            _redundancyCancellationTokenSource.Token,
                            progress,
                            analysisProgress),
                    _redundancyCancellationTokenSource.Token);

                while (!pendingGroups.IsEmpty)
                {
                    FlushPendingRedundancyGroups(
                        pendingGroups,
                        totalBytes,
                        100);

                    if (!pendingGroups.IsEmpty)
                    {
                        await Task.Delay(50);
                    }
                }

                _redundancyAnalysisProgressText =
                    $"100 % ({LocalizationService.GetText("Advanced.Redundancy.Progress.Completed")})";
                _redundancyAnalysisProgressBar.Value = 1F;
                _redundancyAnalysisProgressBar.Invalidate();
                _redundanciesLoaded = true;
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                updateTimer.Stop();
                _redundancyGrid.AnimationTime =
                    previousAnimationTime;
                _redundanciesLoading = false;
            }
        }

        // Do not refresh while rows are expanded; refreshes can collapse or flicker the tree.
        private void FlushPendingRedundancyGroups(
            ConcurrentQueue<RedundancyAnalysisGroup> pendingGroups,
            long totalBytes,
            int maximumGroups,
            bool forceRefresh = false)
        {
            if (IsDisposed ||
                Disposing ||
                pendingGroups == null ||
                maximumGroups <= 0)
            {
                return;
            }

            if (!forceRefresh &&
                _expandedRedundancyRows.Count > 0)
            {
                return;
            }

            int addedGroups = 0;

            _redundancyGrid.PauseLayout = true;

            try
            {
                while (addedGroups < maximumGroups &&
                    pendingGroups.TryDequeue(
                        out RedundancyAnalysisGroup group))
                {
                    RedundancyRow row =
                        new RedundancyRow
                        {
                            Name = group.Name,
                            UsagePercent =
                                totalBytes > 0
                                    ? group.TotalSizeBytes *
                                        100D /
                                        totalBytes
                                    : 0D,
                            Count =
                                group.PhysicalCopyCount,
                            SizeBytes =
                                group.SizeBytes,
                            TotalSizeBytes =
                                group.TotalSizeBytes,
                            IsLocation = false,
                            Children =
                                group.Locations
                                    .Select(path =>
                                        new RedundancyRow
                                        {
                                            Name = path,
                                            IsLocation = true,
                                            Children =
                                                new List<RedundancyRow>()
                                        })
                                    .ToList()
                        };

                    int insertIndex =
                        FindRedundancyInsertIndex(
                            row.TotalSizeBytes ?? 0L);

                    _redundancyRows.Insert(
                        insertIndex,
                        row);

                    addedGroups++;
                }
            }
            finally
            {
                _redundancyGrid.PauseLayout = false;
            }

            if (addedGroups > 0)
            {
                List<RedundancyRow> expandedRows =
                    _expandedRedundancyRows
                        .ToList();

                _redundancyGrid.Refresh(
                    _redundancyRows);

                foreach (RedundancyRow expandedRow in
                    expandedRows)
                {
                    if (_redundancyRows.Contains(
                            expandedRow))
                    {
                        _redundancyGrid.Expand(
                            expandedRow,
                            true);
                    }
                }
            }
        }

        private void RedundancyGrid_ExpandChanged(
            object sender,
            AntdUI.TableExpandEventArgs e)
        {
            if (e.Record is not RedundancyRow row ||
                row.IsLocation)
            {
                return;
            }

            if (e.Expand)
            {
                _expandedRedundancyRows.Add(
                    row);
            }
            else
            {
                _expandedRedundancyRows.Remove(
                    row);
            }
        }

        private void RedundancyGrid_MouseDown(
            object sender,
            MouseEventArgs e)
        {
            _redundancyContextRow = null;
        }

        private void RedundancyGrid_CellHover(
            object sender,
            AntdUI.TableHoverEventArgs e)
        {
            dynamic eventArgs = e;

            RedundancyRow row =
                eventArgs.Record as RedundancyRow;
            AntdUI.Column column =
                eventArgs.Column as AntdUI.Column;

            if (row == null ||
                !row.IsLocation ||
                column == null ||
                !string.Equals(
                    column.Key,
                    nameof(RedundancyRow.Name),
                    StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(
                    row.Name))
            {
                _redundancyGrid.CloseTip();
                return;
            }

            Rectangle rect =
                eventArgs.Rect;

            _redundancyGrid.OpenTip(
                rect,
                row.Name);
        }

        private void RedundancyGrid_CellClickBegin(
            object sender,
            AntdUI.TableClickBeginEventArgs e)
        {
            dynamic eventArgs = e;

            _redundancyContextRow =
                eventArgs.Record as RedundancyRow;
        }

        private void RedundancyOpenParentFolder_Click(
            object sender,
            EventArgs e)
        {
            if (_redundancyContextRow == null ||
                !_redundancyContextRow.IsLocation ||
                string.IsNullOrWhiteSpace(
                    _redundancyContextRow.Name))
            {
                return;
            }

            Process.Start(
                new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments =
                        "/select,\"" +
                        _redundancyContextRow.Name +
                        "\"",
                    UseShellExecute = true
                });
        }

        private int FindRedundancyInsertIndex(
            long totalSizeBytes)
        {
            int low = 0;
            int high = _redundancyRows.Count;

            while (low < high)
            {
                int middle =
                    low + ((high - low) / 2);

                long middleSize =
                    _redundancyRows[middle]
                        .TotalSizeBytes ??
                    0L;

                if (middleSize >= totalSizeBytes)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            return low;
        }

        // Stabilizes rapidly changing analysis phases to prevent progress-text flicker.
        private void UpdateRedundancyProgressPhase(
            RedundancyAnalysisPhase phase)
        {
            if (phase == RedundancyAnalysisPhase.Completed)
            {
                _displayedRedundancyProgressPhase =
                    RedundancyAnalysisPhase.Completed;
                _pendingRedundancyProgressPhase =
                    RedundancyAnalysisPhase.Completed;
                _pendingRedundancyProgressPhaseSince =
                    DateTime.UtcNow;
                return;
            }

            RedundancyAnalysisPhase displayPhase =
                phase switch
                {
                    RedundancyAnalysisPhase.FirstBlock =>
                        RedundancyAnalysisPhase.FullHashLive,
                    RedundancyAnalysisPhase.LastBlock =>
                        RedundancyAnalysisPhase.FullHashLive,
                    RedundancyAnalysisPhase.FullHashLive =>
                        RedundancyAnalysisPhase.FullHashLive,
                    RedundancyAnalysisPhase.FullHashCache =>
                        RedundancyAnalysisPhase.FullHashCache,
                    _ => phase
                };

            bool isReadPhase =
                displayPhase ==
                    RedundancyAnalysisPhase.FullHashLive ||
                displayPhase ==
                    RedundancyAnalysisPhase.FullHashCache;

            bool pendingIsReadPhase =
                _pendingRedundancyProgressPhase ==
                    RedundancyAnalysisPhase.FullHashLive ||
                _pendingRedundancyProgressPhase ==
                    RedundancyAnalysisPhase.FullHashCache;

            if (!isReadPhase &&
                pendingIsReadPhase)
            {
                return;
            }

            if (displayPhase !=
                _pendingRedundancyProgressPhase)
            {
                _pendingRedundancyProgressPhase =
                    displayPhase;
                _pendingRedundancyProgressPhaseSince =
                    DateTime.UtcNow;
                return;
            }

            if (_displayedRedundancyProgressPhase !=
                    _pendingRedundancyProgressPhase &&
                DateTime.UtcNow -
                    _pendingRedundancyProgressPhaseSince >=
                    TimeSpan.FromSeconds(1))
            {
                _displayedRedundancyProgressPhase =
                    _pendingRedundancyProgressPhase;
            }
        }

        private static string GetRedundancyProgressAction(
            RedundancyAnalysisPhase phase)
        {
            return phase switch
            {
                RedundancyAnalysisPhase.SizeGrouping =>
                    LocalizationService.GetText(
                        "Advanced.Redundancy.Progress.SizeGrouping"),
                RedundancyAnalysisPhase.FirstBlock =>
                    LocalizationService.GetText(
                        "Advanced.Redundancy.Progress.LiveRead"),
                RedundancyAnalysisPhase.LastBlock =>
                    LocalizationService.GetText(
                        "Advanced.Redundancy.Progress.LiveRead"),
                RedundancyAnalysisPhase.FullHashLive =>
                    LocalizationService.GetText(
                        "Advanced.Redundancy.Progress.LiveRead"),
                RedundancyAnalysisPhase.FullHashCache =>
                    LocalizationService.GetText(
                        "Advanced.Redundancy.Progress.CacheRead"),
                RedundancyAnalysisPhase.FileIdentity =>
                    LocalizationService.GetText(
                        "Advanced.Redundancy.Progress.FileIdentity"),
                RedundancyAnalysisPhase.Cache =>
                    LocalizationService.GetText(
                        "Advanced.Redundancy.Progress.CacheSave"),
                RedundancyAnalysisPhase.Completed =>
                    LocalizationService.GetText(
                        "Advanced.Redundancy.Progress.Completed"),
                _ =>
                    LocalizationService.GetText(
                        "Advanced.Redundancies")
            };
        }

        private AntdUI.TabPage CreateLargestFilesPage()
        {
            _largestFilesGrid.Columns = CreateLargestFilesColumns();
            _largestFilesGrid.AutoSizeColumnsMode =
                AntdUI.ColumnsMode.Auto;
            _largestFilesGrid.TooltipConfig =
                new AntdUI.TooltipConfig
                {
                    CustomWidth =
                        Math.Max(
                            1,
                            Screen.FromControl(
                                _largestFilesGrid)
                                .Bounds.Width / 4)
                };
            _largestFilesGrid.CellHover +=
                LargestFilesGrid_CellHover;
            _largestFilesGrid.MouseDown +=
                LargestFilesGrid_MouseDown;
            _largestFilesGrid.CellClickBegin +=
                LargestFilesGrid_CellClickBegin;
            _largestFilesGrid.CellClick +=
                LargestFilesGrid_CellClick;
            _largestFilesGrid.CellDoubleClick +=
                LargestFilesGrid_CellDoubleClick;

            ToolStripMenuItem openParentFolderItem =
                new ToolStripMenuItem(
                    LocalizationService.GetText(
                        "Search.OpenParentFolder"));
            openParentFolderItem.Click +=
                LargestFilesOpenParentFolder_Click;
            _largestFilesContextMenu.Items.Add(
                openParentFolderItem);
            _largestFilesContextMenu.Opening +=
                (sender, e) =>
                    e.Cancel =
                        _largestFilesContextRow == null;
            AntdThemeService.ConfigureContextMenu(
                _largestFilesContextMenu);
            _largestFilesGrid.ContextMenuStrip =
                _largestFilesContextMenu;

            _largestFilesGrid.SetResponsiveColumns(
                (
                    nameof(LargestFileRow.Name),
                    AntdThemeService.AnalysisLargestFilesNameColumnWidthPercent
                ));

            return CreatePage(
                LocalizationService.GetText("Advanced.LargestFiles"),
                _largestFilesGrid);
        }

        private AntdUI.ColumnCollection CreateLargestFilesColumns()
        {
            return new AntdUI.ColumnCollection
            {
                new AntdUI.Column(
                    nameof(LargestFileRow.Name),
                    LocalizationService.GetText("Common.Name"))
                {
                    Ellipsis = true,
                    SortOrder = true,
                    Render = (value, record, rowIndex) =>
                    {
                        string name = record is LargestFileRow row
                            ? row.Name
                            : value?.ToString();

                        return AntdThemeService.CreateVisibleTableCellText(name);
                    }
                },
                new AntdUI.Column(
                    nameof(LargestFileRow.UsagePercent),
                    LocalizationService.GetText("Advanced.Usage"),
                    AntdUI.ColumnAlign.Center)
                {
                    Width =
                        (AntdThemeService.TableProgressWidth +
                         (AntdThemeService.TableCellHorizontalPadding * 2))
                        .ToString(),
                    SortOrder = true,
                    Render = (value, record, rowIndex) =>
                    {
                        double percent = record is LargestFileRow row
                            ? row.UsagePercent
                            : 0D;

                        return new AnalysisPercentCellProgress(
                            (float)Math.Clamp(
                                percent / 100D,
                                0D,
                                1D),
                            $"{percent:0.0} %");
                    }
                },
                new AntdUI.Column(
                    nameof(LargestFileRow.FormattedSize),
                    LocalizationService.GetText("Advanced.SizeGb"),
                    AntdUI.ColumnAlign.Right)
                {
                    Width = "auto",
                    Ellipsis = true,
                    SortOrder = true
                },
                new AntdUI.Column(
                    nameof(LargestFileRow.SizeBytes),
                    GetSizeUnitHeader(),
                    AntdUI.ColumnAlign.Right)
                {
                    Width = "auto",
                    Ellipsis = true,
                    SortOrder = true,
                    Render = (value, record, rowIndex) =>
                    {
                        long sizeBytes = record is LargestFileRow row
                            ? row.SizeBytes
                            : 0L;

                        return FormatSizeValue(sizeBytes);
                    }
                },
                new AntdUI.Column(
                    nameof(LargestFileRow.LastWriteTime),
                    LocalizationService.GetText("Advanced.Modified"))
                {
                    Width = "auto",
                    Ellipsis = true,
                    SortOrder = true,
                    Render = (value, record, rowIndex) =>
                    {
                        string modified =
                            record is LargestFileRow row &&
                            row.LastWriteTime != DateTime.MinValue
                                ? row.LastWriteTime.ToString("g")
                                : string.Empty;

                        return AntdThemeService.CreateVisibleTableCellText(
                            modified);
                    }
                },
                new AntdUI.Column(
                    nameof(LargestFileRow.FullPath),
                    LocalizationService.GetText("Common.Path"))
                {
                    Width = "fill",
                    Ellipsis = true,
                    SortOrder = true,
                    Render = (value, record, rowIndex) =>
                    {
                        string path = record is LargestFileRow row
                            ? row.FullPath
                            : value?.ToString();

                        return AntdThemeService.CreateVisibleTableCellText(path);
                    }
                }
            };
        }

        private void RefreshData()
        {
            
            List<FileSystemEntry> files = GetFiles();
            long totalFileTypeBytes =
                files.Sum(file => file.SizeBytes);

            _fileTypeRows = files
                .GroupBy(file => string.IsNullOrWhiteSpace(
                    Path.GetExtension(file.Name))
                    ? LocalizationService.GetText(
                        "Advanced.NoExtension")
                    : Path.GetExtension(file.Name)
                        .ToLowerInvariant())
                .Select(group =>
                {
                    long sizeBytes =
                        group.Sum(file => file.SizeBytes);

                    return new FileTypeRow
                    {
                        Extension = group.Key,
                        UsagePercent = totalFileTypeBytes > 0
                            ? sizeBytes * 100D /
                                totalFileTypeBytes
                            : 0D,
                        SizeGb =
                            (sizeBytes /
                             (1024D * 1024D * 1024D))
                            .ToString("N2") + " GB",
                        SizeMb =
                            (sizeBytes /
                             (1024D * 1024D))
                            .ToString("N0") + " MB",
                        SizeBytes = sizeBytes
                    };
                })
                .OrderByDescending(row => row.SizeBytes)
                .ToList();

            _fileTypeCategoryRows = files
                .GroupBy(file =>
                    FileTypeCategories.GetCategoryKey(file.Name))
                .Select(group =>
                {
                    long sizeBytes =
                        group.Sum(file => file.SizeBytes);

                    return new FileTypeCategoryRow
                    {
                        FileType =
                            LocalizationService.GetText(
                                group.Key),
                        UsagePercent = totalFileTypeBytes > 0
                            ? sizeBytes * 100D /
                                totalFileTypeBytes
                            : 0D,
                        SizeGb =
                            (sizeBytes /
                             (1024D * 1024D * 1024D))
                            .ToString("N2") + " GB",
                        SizeMb =
                            (sizeBytes /
                             (1024D * 1024D))
                            .ToString("N0") + " MB",
                        SizeBytes = sizeBytes
                    };
                })
                .OrderByDescending(row => row.SizeBytes)
                .ToList();

            _largestFileRows = files
                .OrderByDescending(file => file.SizeBytes)
                .Take(1000)
                .Select(file => new LargestFileRow
                {
                    Name = file.Name,
                    UsagePercent = totalFileTypeBytes > 0
                        ? file.SizeBytes * 100D /
                            totalFileTypeBytes
                        : 0D,
                    FormattedSize =
                        SizeFormatter.Format(file.SizeBytes),
                    SizeBytes = file.SizeBytes,
                    LastWriteTime =
                        file.LastWriteTimeUtc ==
                            DateTime.MinValue
                            ? DateTime.MinValue
                            : file.LastWriteTimeUtc
                                .ToLocalTime(),
                    FullPath = file.FullPath
                })
                .ToList();

            _fileTypeGrid.DataSource = _fileTypeRows;
            _fileTypeCategoryGrid.DataSource =
                _fileTypeCategoryRows;
            _largestFilesGrid.DataSource =
                _largestFileRows;
        }

        private void LargestFilesGrid_MouseDown(
            object sender,
            MouseEventArgs e)
        {
            _largestFilesContextRow = null;
        }

        private void LargestFilesGrid_CellHover(
            object sender,
            AntdUI.TableHoverEventArgs e)
        {
            dynamic eventArgs = e;

            LargestFileRow row =
                eventArgs.Record as LargestFileRow;
            AntdUI.Column column =
                eventArgs.Column as AntdUI.Column;

            if (row == null ||
                column == null ||
                (!string.Equals(
                    column.Key,
                    nameof(LargestFileRow.Name),
                    StringComparison.Ordinal) &&
                 !string.Equals(
                    column.Key,
                    nameof(LargestFileRow.FullPath),
                    StringComparison.Ordinal)) ||
                string.IsNullOrWhiteSpace(
                    row.FullPath))
            {
                _largestFilesGrid.CloseTip();
                return;
            }

            Rectangle rect =
                eventArgs.Rect;

            _largestFilesGrid.OpenTip(
                rect,
                row.FullPath);
        }

        private void LargestFilesGrid_CellClickBegin(
            object sender,
            AntdUI.TableClickBeginEventArgs e)
        {
            dynamic eventArgs = e;

            _largestFilesContextRow =
                eventArgs.Record as LargestFileRow;
        }

        private void LargestFilesGrid_CellClick(
            object sender,
            AntdUI.TableClickEventArgs e)
        {
            dynamic eventArgs = e;
            object record = eventArgs.Record;
            AntdUI.Column column = eventArgs.Column;

            _largestFilesContextRow =
                record as LargestFileRow;

            if (record != null ||
                column == null ||
                !string.Equals(
                    column.Key,
                    nameof(LargestFileRow.SizeBytes),
                    StringComparison.Ordinal))
            {
                return;
            }

            CycleSizeUnit();
        }

        private void LargestFilesGrid_CellDoubleClick(
            object sender,
            AntdUI.TableClickEventArgs e)
        {
            dynamic eventArgs = e;

            if (eventArgs.Record is not LargestFileRow selectedRow)
                return;

            OpenSelectedFile(selectedRow);
        }

        private void LargestFilesOpenParentFolder_Click(
            object sender,
            EventArgs e)
        {
            if (_largestFilesContextRow == null)
                return;

            OpenSelectedFile(_largestFilesContextRow);
        }

        private void CycleSizeUnit()
        {
            _sizeUnit = _sizeUnit switch
            {
                SizeUnit.Bytes => SizeUnit.KB,
                SizeUnit.KB => SizeUnit.MB,
                SizeUnit.MB => SizeUnit.GB,
                SizeUnit.GB => SizeUnit.TB,
                _ => SizeUnit.Bytes
            };

            AntdUI.Column sizeColumn =
                _largestFilesGrid.Columns.FirstOrDefault(
                    column => string.Equals(
                        column.Key,
                        nameof(LargestFileRow.SizeBytes),
                        StringComparison.Ordinal));

            if (sizeColumn != null)
                sizeColumn.Title = GetSizeUnitHeader();

            _largestFilesGrid.LoadLayout();
            _largestFilesGrid.Invalidate();
        }

        private string GetSizeUnitHeader()
        {
            return $"{LocalizationService.GetText("Common.Size")} ({_sizeUnit})";
        }

        private string FormatSizeValue(long sizeBytes)
        {
            double divisor = _sizeUnit switch
            {
                SizeUnit.KB => 1024D,
                SizeUnit.MB => 1024D * 1024D,
                SizeUnit.GB => 1024D * 1024D * 1024D,
                SizeUnit.TB =>
                    1024D * 1024D * 1024D * 1024D,
                _ => 1D
            };

            if (_sizeUnit == SizeUnit.Bytes)
                return sizeBytes.ToString("N0");

            if (_sizeUnit == SizeUnit.MB)
            {
                return (sizeBytes / divisor).ToString("N0") +
                    " MB";
            }

            return (sizeBytes / divisor).ToString("N2");
        }

        // Prefer the scan's flat AllFiles source; recurse only as fallback.
        private List<FileSystemEntry> GetFiles()
        {
            if (_rootEntry.AllFiles != null &&
                _rootEntry.AllFiles.Count > 0)
            {
                return _rootEntry.AllFiles
                    .Where(file =>
                        file != null &&
                        !file.IsDirectory)
                    .ToList();
            }

            List<FileSystemEntry> files =
                new List<FileSystemEntry>();

            CollectFiles(_rootEntry, files);
            return files;
        }

        private static void CollectFiles(
            FileSystemEntry entry,
            List<FileSystemEntry> files)
        {
            if (entry == null)
                return;

            foreach (FileSystemEntry child in entry.Children)
            {
                if (child.IsDirectory)
                    CollectFiles(child, files);
                else
                    files.Add(child);
            }
        }

        private static AntdUI.TabPage CreatePage(
            string title,
            Control control)
        {
            AntdUI.TabPage page = new AntdUI.TabPage
            {
                Text = title,
                BackColor =
                    AntdThemeService.BackgroundPrimary,
                ForeColor =
                    AntdThemeService.TextPrimary,
                Padding = Padding.Empty
            };

            control.Dock = DockStyle.Fill;
            page.Controls.Add(control);
            return page;
        }

        private void ApplyTheme()
        {
            BackColor =
                AntdThemeService.BackgroundPrimary;
            ForeColor =
                AntdThemeService.TextPrimary;

            _fileTypeGrid.ApplyAntdUIStyle();
            _fileTypeCategoryGrid.ApplyAntdUIStyle();
            _redundancyGrid.ApplyAntdUIStyle();
            _largestFilesGrid.ApplyAntdUIStyle();
        }

        // Closing this form cancels its running redundancy analysis.
        protected override void OnFormClosing(
            FormClosingEventArgs e)
        {
            _redundancyCancellationTokenSource.Cancel();
            base.OnFormClosing(e);
        }

        protected override void Dispose(
            bool disposing)
        {
            if (disposing)
            {
                _redundancyCancellationTokenSource.Dispose();
            }

            base.Dispose(disposing);
        }

        private static void OpenSelectedFile(
            LargestFileRow selectedRow)
        {
            string path = selectedRow?.FullPath;

            if (string.IsNullOrWhiteSpace(path))
                return;

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = File.Exists(path)
                    ? "/select,\"" + path + "\""
                    : "\"" + path + "\"",
                UseShellExecute = true
            });
        }

        private sealed class AnalysisPercentCellProgress :
            AntdUI.CellProgress
        {
            private readonly string _text;

            public AnalysisPercentCellProgress(
                float value,
                string text)
                : base(value)
            {
                _text = text;
                Radius =
                    AntdThemeService.TableProgressRadius;
                Back =
                    AntdThemeService.TableProgressBackColor;
                Fill =
                    AntdThemeService.TableProgressFillColor;
                Size = new Size(
                    AntdThemeService.TableProgressWidth,
                    AntdThemeService.TableProgressHeight);
            }

            public override void Paint(
                AntdUI.Canvas g,
                Font font,
                bool enable,
                SolidBrush fore)
            {
                base.Paint(g, font, enable, fore);
                g.String(_text, font, fore, Rect);
            }
        }
    }
}
