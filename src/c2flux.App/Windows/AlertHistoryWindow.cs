using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace c2flux
{
    // Port of the WinForms AlertHistoryForm ("Short log"): the alerts with
    // their severity, the details of the selected one, and buttons to confirm
    // or delete them. Opened from the status bar counters.
    public sealed class AlertHistoryWindow : Window
    {
        private readonly DrawnTable<AppAlertEntry> _table = new DrawnTable<AppAlertEntry> { Style = TableStyle.Classic, MultiSelect = true };
        private readonly TextBox _details = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true };
        private readonly Button _confirm = Button("AlertHistory.Confirm", 95);
        private readonly Button _delete = Button("AlertHistory.Delete", 85);
        private readonly Button _confirmAll = Button("AlertHistory.ConfirmAll", 110);
        private readonly Button _deleteAll = Button("AlertHistory.DeleteAll", 95);
        private readonly Button _close = Button("Common.Close", 90);

        public AlertHistoryWindow()
        {
            Title = LocalizationService.GetText("AlertHistory.Title");
            Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://c2flux.App/Assets/c2flux.png")));
            Width = 820;
            Height = 500;
            MinWidth = 640;
            MinHeight = 380;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            _table.Columns.Add(new TableColumn<AppAlertEntry>("Info", entry => entry.SeverityText)
            {
                Width = 50,
                SortKey = entry => entry.Severity,
                Paint = (context, cell, entry, _) => StatusSymbolRenderer.DrawSymbol(
                    context,
                    new Rect(Math.Round(cell.Center.X - 7), Math.Round(cell.Center.Y - 7), 14, 14),
                    entry.Severity == AppAlertSeverity.Warning ? StatusSymbolKind.Warning
                        : entry.Severity == AppAlertSeverity.Error ? StatusSymbolKind.Error
                        : StatusSymbolKind.Information),
            });
            _table.Columns.Add(new TableColumn<AppAlertEntry>(LocalizationService.GetText("AlertHistory.Category"), entry => entry.Category) { Width = 140 });
            _table.Columns.Add(new TableColumn<AppAlertEntry>(LocalizationService.GetText("AlertHistory.Message"), entry => entry.Message) { Width = 100, Fill = true });
            _table.Columns.Add(new TableColumn<AppAlertEntry>(LocalizationService.GetText("AlertHistory.CreatedAt"), entry => entry.CreatedAtText) { Width = 140, SortKey = entry => entry.CreatedAt });
            _table.Columns.Add(new TableColumn<AppAlertEntry>(LocalizationService.GetText("AlertHistory.Confirmed"), entry => entry.ConfirmedText) { Width = 80, SortKey = entry => entry.IsConfirmed });
            _table.SelectionChanged += _ => UpdateState();

            _confirm.Click += (_, _) => AppAlertLog.Confirm(_table.SelectedItems.Select(entry => entry.Id).ToList());
            _delete.Click += (_, _) => AppAlertLog.Delete(_table.SelectedItems.Select(entry => entry.Id).ToList());
            _confirmAll.Click += (_, _) => AppAlertLog.ConfirmAll();
            _deleteAll.Click += (_, _) => AppAlertLog.DeleteAll();
            _close.Click += (_, _) => Close();
            _close.IsDefault = true;
            _close.IsCancel = true;

            StackPanel buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 6,
                Margin = new Thickness(0, 8),
                Children = { _confirm, _delete, _confirmAll, _deleteAll, _close },
            };
            DockPanel details = new DockPanel();
            TextBlock detailsLabel = new TextBlock { Text = LocalizationService.GetText("AlertHistory.Details"), Height = 20, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(detailsLabel, Dock.Top);
            details.Children.Add(detailsLabel);
            details.Children.Add(Sunken(_details));

            // WinForms: 8 px panel padding plus the 3 px default margin of each
            // TableLayoutPanel cell (and the table's own 7 px inset).
            Grid layout = new Grid { RowDefinitions = new RowDefinitions("*,160,48"), Margin = new Thickness(18, 11, 18, 0) };
            Grid.SetRow(details, 1);
            Grid.SetRow(buttons, 2);
            layout.Children.Add(_table);
            layout.Children.Add(details);
            layout.Children.Add(buttons);
            Content = layout;

            AppAlertLog.Changed += OnAlertLogChanged;
            Closed += (_, _) => AppAlertLog.Changed -= OnAlertLogChanged;
            LoadAlerts();
        }

        private void OnAlertLogChanged(object sender, EventArgs e) => Dispatcher.UIThread.Post(LoadAlerts);

        private void LoadAlerts()
        {
            _table.SetItems(AppAlertLog.GetEntries());

            // A bound DataGridView starts with its first row selected.
            if (_table.SelectedItems.Count == 0 && _table.Items.Count > 0)
            {
                _table.Select(_table.Items[0]);
            }

            UpdateState();
        }

        // One selected alert shows its details (or message); buttons follow
        // the selection.
        private void UpdateState()
        {
            IReadOnlyList<AppAlertEntry> selected = _table.SelectedItems;
            _confirm.IsEnabled = selected.Count > 0;
            _delete.IsEnabled = selected.Count > 0;
            _confirmAll.IsEnabled = _table.Items.Count > 0;
            _deleteAll.IsEnabled = _table.Items.Count > 0;
            _details.Text = selected.Count == 1
                ? !string.IsNullOrWhiteSpace(selected[0].Details) ? selected[0].Details : selected[0].Message ?? string.Empty
                : string.Empty;
        }

        // The Fixed3D border of a WinForms RichTextBox: dark top-left, light
        // bottom-right, two pixels each.
        private static Control Sunken(TextBox box)
        {
            box.Background = Brushes.Transparent;
            box.BorderThickness = new Thickness(0);
            box.CornerRadius = new CornerRadius(0);
            box.Padding = new Thickness(1);
            Border inner = new Border
            {
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(105, 105, 105)),
                Child = box,
            };
            inner.Bind(Border.BackgroundProperty, inner.GetResourceObservable("BackgroundPrimaryBrush"));
            return new Border
            {
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(160, 160, 160)),
                Child = new Border
                {
                    BorderThickness = new Thickness(0, 0, 1, 1),
                    BorderBrush = Brushes.White,
                    Child = inner,
                },
            };
        }

        private static Button Button(string textKey, double width)
        {
            return new Button
            {
                Content = LocalizationService.GetText(textKey),
                Width = width,
                Height = 30,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Classes = { "ant" },
            };
        }
    }
}
