using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace c2flux
{
    // A date that can be typed, or picked in a month calendar under the field.
    // Weeks start on Monday. The look is in Themes/Controls.axaml (cal*).
    public sealed class DateField : UserControl
    {
        private const string DateFormat = "dd.MM.yyyy";
        private const int DaysInGrid = 42;
        private const int YearsInGroup = 20;
        private static readonly DateTime FirstDate = new DateTime(1900, 1, 1);
        private static readonly DateTime LastDate = new DateTime(2099, 12, 31);

        private readonly TextBox _box;
        private readonly Popup _popup;
        private readonly ContentControl _body = new ContentControl();
        private readonly Button _monthButton = Header();
        private readonly Button _yearButton = Header();
        private readonly Button _previous = Arrow("M 5,0 L 0,5 L 5,10");
        private readonly Button _next = Arrow("M 0,0 L 5,5 L 0,10");
        private readonly UniformGrid _names = new UniformGrid { Columns = 7, Margin = new Thickness(0, 0, 0, 4) };
        private readonly UniformGrid _days = new UniformGrid { Columns = 7 };
        private readonly DockPanel _dayView = new DockPanel();
        private DateTime? _selected;
        private DateTime _month;
        private long _closedAt;

        public DateField()
        {
            _box = new TextBox { Classes = { "ant" }, VerticalAlignment = VerticalAlignment.Stretch };
            Button open = new Button
            {
                Classes = { "calicon" },
                // A fixed box: a bare path keeps the offset its coordinates start at.
                Content = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse("M 0,2 H 12 V 12 H 0 Z M 0,5.5 H 12 M 3.5,0 V 3 M 8.5,0 V 3"), Width = 14, Height = 14, Stretch = Stretch.Uniform },
            };
            Avalonia.Automation.AutomationProperties.SetName(open, LocalizationService.GetText("StorageHistory.Calendar"));
            _box.InnerRightContent = open;
            _box.LostFocus += (_, _) => CommitText();
            _box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    CommitText();
                }
            };
            open.Click += (_, _) => ToggleCalendar();

            _monthButton.Click += (_, _) => ShowMonths();
            _yearButton.Click += (_, _) => ShowYearGroups();
            _previous.Click += (_, _) => ShowDays(_month.AddMonths(-1));
            _next.Click += (_, _) => ShowDays(_month.AddMonths(1));
            _body.AddHandler(KeyDownEvent, OnCalendarKeyDown, RoutingStrategies.Tunnel);
            DockPanel.SetDock(_names, Dock.Top);
            _dayView.Children.AddRange(new Control[] { _names, _days });

            Grid header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,Auto"), Margin = new Thickness(0, 0, 0, 10) };
            Grid.SetColumn(_yearButton, 1);
            Grid.SetColumn(_previous, 3);
            Grid.SetColumn(_next, 4);
            _next.Margin = new Thickness(6, 0, 0, 0);
            header.Children.AddRange(new Control[] { _monthButton, _yearButton, _previous, _next });
            DockPanel.SetDock(header, Dock.Top);

            CalendarRoot = new Border
            {
                Classes = { "calpanel" },
                Width = 300,
                Child = new DockPanel { Children = { header, _body } },
            };
            CalendarRoot.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    _popup.IsOpen = false;
                    _box.Focus();
                }
            };

            _popup = new Popup
            {
                PlacementTarget = _box,
                Placement = PlacementMode.BottomEdgeAlignedLeft,
                IsLightDismissEnabled = true,
                Child = CalendarRoot,
            };
            // Opened by pointer: focused, with no ring until a key moves it.
            _popup.Opened += (_, _) => Dispatcher.UIThread.Post(() => FocusDay(_selected ?? DateTime.Today, NavigationMethod.Unspecified));
            _popup.Closed += (_, _) => _closedAt = Environment.TickCount64;

            Content = new Panel { Children = { _box, _popup } };
            ShowDays(FirstOfMonth(DateTime.Today));
        }

        public event EventHandler SelectedDateChanged;

        public DateTime? SelectedDate
        {
            get => _selected;
            set
            {
                DateTime? date = value == null ? null : Clamp(value.Value.Date);
                _box.Text = date?.ToString(DateFormat, CultureInfo.InvariantCulture) ?? string.Empty;

                if (date != _selected)
                {
                    _selected = date;
                    SelectedDateChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        internal Control CalendarRoot { get; }
        internal Button MonthButton => _monthButton;
        internal Button YearButton => _yearButton;
        internal Button PreviousButton => _previous;
        internal Button NextButton => _next;
        internal bool IsCalendarOpen => _popup.IsOpen;
        internal IEnumerable<Button> Cells => (_body.Content as Control)?.GetLogicalButtons() ?? Enumerable.Empty<Button>();
        internal void OpenCalendar() => ToggleCalendar();
        internal void ShowMonthOf(DateTime date) => ShowDays(FirstOfMonth(date));
        internal void HandleCalendarKey(Key key) => OnCalendarKeyDown(this, new KeyEventArgs { Key = key, RoutedEvent = KeyDownEvent });
        internal DateTime? FocusedDay => _days.Children.OfType<Button>().FirstOrDefault(button => button.IsTabStop)?.Tag as DateTime?;

        // The first cell of a month's grid: the Monday on or before its 1st.
        internal static DateTime FirstCell(DateTime month)
        {
            DateTime first = FirstOfMonth(month);
            return first.AddDays(-(((int)first.DayOfWeek + 6) % 7));
        }

        private static DateTime FirstOfMonth(DateTime date) => new DateTime(date.Year, date.Month, 1);

        private static DateTime Clamp(DateTime date) => date < FirstDate ? FirstDate : date > LastDate ? LastDate : date;

        private static CultureInfo Culture()
        {
            try
            {
                return CultureInfo.GetCultureInfo(LocalizationService.CurrentLanguageCode);
            }
            catch (CultureNotFoundException)
            {
                return CultureInfo.InvariantCulture;
            }
        }

        private static string MonthName(int month)
        {
            CultureInfo culture = Culture();
            return culture.TextInfo.ToTitleCase(culture.DateTimeFormat.GetAbbreviatedMonthName(month).TrimEnd('.'));
        }

        private void CommitText()
        {
            string text = _box.Text?.Trim();

            if (string.IsNullOrEmpty(text))
            {
                SelectedDate = null;
            }
            else if (DateTime.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date) && date >= FirstDate && date <= LastDate)
            {
                SelectedDate = date;
            }
            else
            {
                // Not a date: back to the last one.
                SelectedDate = _selected;
            }
        }

        private void ToggleCalendar()
        {
            // A click outside closes the calendar before the icon hears it:
            // that click on the icon means close, not open again.
            if (_popup.IsOpen || Environment.TickCount64 - _closedAt < 200)
            {
                _popup.IsOpen = false;
                return;
            }

            CommitText();
            ShowDays(FirstOfMonth(_selected ?? DateTime.Today));
            _popup.IsOpen = true;
        }

        private void ShowDays(DateTime month)
        {
            _month = Clamp(month);
            _month = FirstOfMonth(_month);
            ShowNavigation(true);
            _monthButton.Content = HeaderContent(MonthName(_month.Month));
            _yearButton.Content = HeaderContent(_month.Year.ToString(CultureInfo.InvariantCulture));
            _previous.IsEnabled = _month > FirstOfMonth(FirstDate);
            _next.IsEnabled = _month < FirstOfMonth(LastDate);

            _names.Children.Clear();
            DateTimeFormatInfo format = Culture().DateTimeFormat;

            for (int day = 0; day < 7; day++)
            {
                _names.Children.Add(new TextBlock
                {
                    Text = format.GetAbbreviatedDayName((DayOfWeek)((day + 1) % 7)).TrimEnd('.').ToUpper(Culture()),
                    Classes = { "calname" },
                    FontSize = 11,
                    FontWeight = FontWeight.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                });
            }

            _days.Children.Clear();
            DateTime focus = _selected != null && FirstOfMonth(_selected.Value) == _month ? _selected.Value
                : FirstOfMonth(DateTime.Today) == _month ? DateTime.Today
                : _month;
            DateTime first = FirstCell(_month);

            for (int index = 0; index < DaysInGrid; index++)
            {
                DateTime date = first.AddDays(index);
                Button cell = new Button
                {
                    Content = date.Day.ToString(CultureInfo.InvariantCulture),
                    Tag = date,
                    Classes = { "calcell", "day" },
                    IsTabStop = date == focus,
                    HorizontalAlignment = HorizontalAlignment.Center,
                };
                cell.Classes.Set("other", FirstOfMonth(date) != _month);
                cell.Classes.Set("today", date == DateTime.Today);
                cell.Classes.Set("selected", date == _selected);
                cell.Click += (_, _) => Pick(date);
                _days.Children.Add(cell);
            }

            _body.Content = _dayView;
        }

        private void ShowMonths()
        {
            UniformGrid months = new UniformGrid { Columns = 3 };

            for (int month = 1; month <= 12; month++)
            {
                int chosen = month;
                months.Children.Add(Item(MonthName(month), month == _month.Month, () => ShowDays(new DateTime(_month.Year, chosen, 1))));
            }

            ShowNavigation(false);
            _body.Content = months;
        }

        private void ShowYearGroups()
        {
            UniformGrid groups = new UniformGrid { Columns = 2 };

            for (int first = FirstDate.Year; first <= LastDate.Year; first += YearsInGroup)
            {
                int group = first;
                int last = first + YearsInGroup - 1;
                groups.Children.Add(Item(group + "–" + last, _month.Year >= group && _month.Year <= last, () => ShowYears(group)));
            }

            ShowNavigation(false);
            _body.Content = groups;
        }

        private void ShowYears(int first)
        {
            UniformGrid years = new UniformGrid { Columns = 4 };

            for (int year = first; year < first + YearsInGroup; year++)
            {
                int chosen = year;
                years.Children.Add(Item(year.ToString(CultureInfo.InvariantCulture), year == _month.Year, () => ShowDays(new DateTime(chosen, _month.Month, 1))));
            }

            ShowNavigation(false);
            _body.Content = years;
        }

        private void ShowNavigation(bool shown)
        {
            _previous.IsVisible = shown;
            _next.IsVisible = shown;
        }

        private void Pick(DateTime date)
        {
            SelectedDate = date;
            _popup.IsOpen = false;
            _box.Focus();
        }

        private void OnCalendarKeyDown(object sender, KeyEventArgs e)
        {
            int step = e.Key switch
            {
                Key.Left => -1,
                Key.Right => 1,
                Key.Up => -7,
                Key.Down => 7,
                _ => 0,
            };
            DateTime? from = FocusedDay;

            if (step == 0 || from == null || _body.Content != _dayView)
            {
                return;
            }

            e.Handled = true;
            DateTime target = from.Value.AddDays(step);

            if (target < FirstDate || target > LastDate)
            {
                return;
            }

            if (FirstOfMonth(target) != _month)
            {
                ShowDays(FirstOfMonth(target));
            }

            FocusDay(target, NavigationMethod.Directional);
        }

        private void FocusDay(DateTime date, NavigationMethod method)
        {
            foreach (Button cell in _days.Children.OfType<Button>())
            {
                cell.IsTabStop = (DateTime)cell.Tag == date;

                if (cell.IsTabStop && _popup.IsOpen)
                {
                    cell.Focus(method);
                }
            }
        }

        private static Button Item(string text, bool selected, Action chosen)
        {
            Button item = new Button { Content = text, Classes = { "calcell", "item" }, HorizontalAlignment = HorizontalAlignment.Stretch };
            item.Classes.Set("selected", selected);
            item.Click += (_, _) => chosen();
            return item;
        }

        private static Button Header() => new Button { Classes = { "calhead" } };

        private static StackPanel HeaderContent(string text) => new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center },
                new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse("M 0,0 L 4,4 L 8,0"), VerticalAlignment = VerticalAlignment.Center },
            },
        };

        private static Button Arrow(string data) => new Button
        {
            Classes = { "calnav" },
            Content = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse(data) },
        };
    }

    internal static class DateFieldTree
    {
        public static IEnumerable<Button> GetLogicalButtons(this Control root) => root switch
        {
            Button button => new[] { button },
            Panel panel => panel.Children.OfType<Control>().SelectMany(GetLogicalButtons),
            _ => Enumerable.Empty<Button>(),
        };
    }
}
