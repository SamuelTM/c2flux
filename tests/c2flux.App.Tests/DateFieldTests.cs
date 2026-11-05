using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace c2flux.AppTests
{
    public class DateFieldTests
    {
        private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        private static (Window Window, DateField Field, TextBox Box) Show(DateTime? date)
        {
            DateField field = new DateField { SelectedDate = date };
            Window window = new Window { Content = field, Width = 400, Height = 500 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            return (window, field, field.GetVisualDescendants().OfType<TextBox>().First());
        }

        private static string Header(Button button) =>
            ((button.Content as StackPanel)?.Children[0] as TextBlock)?.Text;

        // The calendar of the reference: six weeks from Monday, days around
        // the month marked as such.
        [AvaloniaFact]
        public void The_grid_is_six_weeks_from_monday()
        {
            (Window window, DateField field, _) = Show(new DateTime(2020, 8, 20));

            try
            {
                field.ShowMonthOf(new DateTime(2020, 8, 1));
                Button[] cells = field.Cells.ToArray();

                Assert.Equal(42, cells.Length);
                Assert.Equal(new DateTime(2020, 7, 27), cells[0].Tag);
                Assert.Equal(new DateTime(2020, 9, 6), cells[41].Tag);
                Assert.Equal(cells.Where(cell => cell.Classes.Contains("other")).Select(cell => ((DateTime)cell.Tag).Month).Distinct().OrderBy(month => month), new[] { 7, 9 });
                Assert.Equal(new DateTime(2020, 8, 20), Assert.Single(cells, cell => cell.Classes.Contains("selected")).Tag);
            }
            finally
            {
                window.Close();
            }
        }

        [AvaloniaTheory]
        [InlineData(2024, 2, 2024, 1, 29)]
        [InlineData(2021, 2, 2021, 2, 1)]
        [InlineData(2023, 1, 2022, 12, 26)]
        public void The_first_cell_is_the_monday_on_or_before_the_first(int year, int month, int cellYear, int cellMonth, int cellDay)
        {
            Assert.Equal(new DateTime(cellYear, cellMonth, cellDay), DateField.FirstCell(new DateTime(year, month, 15)));
        }

        [AvaloniaFact]
        public void Today_is_marked_and_picking_a_day_sets_it_and_closes()
        {
            (Window window, DateField field, TextBox box) = Show(new DateTime(2020, 8, 20));
            int changes = 0;
            field.SelectedDateChanged += (_, _) => changes++;

            try
            {
                field.OpenCalendar();
                Assert.True(field.IsCalendarOpen);
                Click(field.Cells.Single(cell => (DateTime)cell.Tag == new DateTime(2020, 9, 2)));

                Assert.Equal(new DateTime(2020, 9, 2), field.SelectedDate);
                Assert.Equal("02.09.2020", box.Text);
                Assert.Equal(1, changes);
                Assert.False(field.IsCalendarOpen);

                field.ShowMonthOf(DateTime.Today);
                Assert.Equal(DateTime.Today, Assert.Single(field.Cells, cell => cell.Classes.Contains("today")).Tag);
            }
            finally
            {
                window.Close();
            }
        }

        [AvaloniaFact]
        public void Month_and_year_are_chosen_from_lists()
        {
            LocalizationService.Load("en");
            (Window window, DateField field, _) = Show(new DateTime(2020, 8, 20));

            try
            {
                field.ShowMonthOf(new DateTime(2020, 8, 1));
                Assert.Equal("Aug", Header(field.MonthButton));
                Assert.Equal("2020", Header(field.YearButton));

                Click(field.MonthButton);
                Assert.False(field.NextButton.IsVisible);
                Assert.Equal(12, field.Cells.Count());
                Click(field.Cells.ElementAt(1));
                Assert.Equal("Feb", Header(field.MonthButton));
                Assert.True(field.NextButton.IsVisible);

                Click(field.YearButton);
                Assert.Equal(new[] { "1900–1919", "1920–1939", "1940–1959", "1960–1979", "1980–1999", "2000–2019", "2020–2039", "2040–2059", "2060–2079", "2080–2099" }, field.Cells.Select(cell => (string)cell.Content));
                Assert.Equal("2020–2039", Assert.Single(field.Cells, cell => cell.Classes.Contains("selected")).Content);
                Click(field.Cells.Single(cell => Equals(cell.Content, "2020–2039")));
                Assert.Equal(Enumerable.Range(2020, 20).Select(year => year.ToString()), field.Cells.Select(cell => (string)cell.Content));
                Assert.Equal("2020", Assert.Single(field.Cells, cell => cell.Classes.Contains("selected")).Content);
                Click(field.Cells.Single(cell => Equals(cell.Content, "2024")));
                Assert.Equal("2024", Header(field.YearButton));
                Assert.Equal(new DateTime(2024, 1, 29), field.Cells.First().Tag);

                Click(field.NextButton);
                Assert.Equal("Mar", Header(field.MonthButton));
                Click(field.PreviousButton);
                Click(field.PreviousButton);
                Click(field.PreviousButton);
                Assert.Equal("Dec", Header(field.MonthButton));
                Assert.Equal("2023", Header(field.YearButton));

                // Browsing picks nothing.
                Assert.Equal(new DateTime(2020, 8, 20), field.SelectedDate);
            }
            finally
            {
                window.Close();
            }
        }

        [AvaloniaFact]
        public void The_limits_stop_the_arrows()
        {
            (Window window, DateField field, _) = Show(null);

            try
            {
                field.ShowMonthOf(new DateTime(1900, 1, 1));
                Assert.False(field.PreviousButton.IsEnabled);
                Assert.True(field.NextButton.IsEnabled);
                field.ShowMonthOf(new DateTime(2099, 12, 1));
                Assert.False(field.NextButton.IsEnabled);
            }
            finally
            {
                window.Close();
            }
        }

        [AvaloniaTheory]
        [InlineData("31.12.2021", 2021, 12, 31)]
        [InlineData("  01.02.2003 ", 2003, 2, 1)]
        [InlineData("31.02.2021", 2020, 8, 20)]
        [InlineData("abc", 2020, 8, 20)]
        [InlineData("01.01.1800", 2020, 8, 20)]
        public void Typed_dates_are_taken_or_undone(string typed, int year, int month, int day)
        {
            (Window window, DateField field, TextBox box) = Show(new DateTime(2020, 8, 20));

            try
            {
                box.Focus();
                box.Text = typed;
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                DateTime expected = new DateTime(year, month, day);
                Assert.Equal(expected, field.SelectedDate);
                Assert.Equal(expected.ToString("dd.MM.yyyy"), box.Text);
            }
            finally
            {
                window.Close();
            }
        }

        [AvaloniaFact]
        public void An_empty_field_is_no_date()
        {
            (Window window, DateField field, TextBox box) = Show(new DateTime(2020, 8, 20));

            try
            {
                box.Focus();
                box.Text = "";
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                Assert.Null(field.SelectedDate);
            }
            finally
            {
                window.Close();
            }
        }

        // In the middle of its button, whatever its coordinates start at.
        [AvaloniaFact]
        public void The_calendar_icon_is_centered_in_its_button()
        {
            (Window window, DateField field, _) = Show(new DateTime(2020, 8, 20));

            try
            {
                Button open = field.GetVisualDescendants().OfType<Button>().Single(button => button.Classes.Contains("calicon"));
                Avalonia.Controls.Shapes.Path icon = open.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single();
                Avalonia.Rect drawn = icon.Data.Bounds;
                Avalonia.Point center = icon.TranslatePoint(new Avalonia.Point(icon.Bounds.Width / 2, icon.Bounds.Height / 2), open).Value;

                Assert.Equal(open.Bounds.Width / 2, center.X, 1);
                Assert.Equal(open.Bounds.Height / 2, center.Y, 1);
                Assert.Equal(0, drawn.X);
                Assert.Equal(0, drawn.Y);
            }
            finally
            {
                window.Close();
            }
        }

        // Arrow keys walk the days, and across months.
        [AvaloniaFact]
        public void Arrow_keys_cross_months()
        {
            (Window window, DateField field, _) = Show(new DateTime(2020, 8, 31));

            try
            {
                field.ShowMonthOf(new DateTime(2020, 8, 1));
                Assert.Equal(new DateTime(2020, 8, 31), field.FocusedDay);
                field.HandleCalendarKey(Key.Right);
                Assert.Equal(new DateTime(2020, 9, 1), field.FocusedDay);
                Assert.Equal(new DateTime(2020, 8, 31), field.Cells.First().Tag);
                field.HandleCalendarKey(Key.Up);
                Assert.Equal(new DateTime(2020, 8, 25), field.FocusedDay);
                // Moving is not picking.
                Assert.Equal(new DateTime(2020, 8, 31), field.SelectedDate);
            }
            finally
            {
                window.Close();
            }
        }

        // Names follow the language of the app.
        [AvaloniaFact]
        public void Names_follow_the_language()
        {
            LocalizationService.Load("pt");
            (Window window, DateField field, _) = Show(new DateTime(2020, 8, 20));

            try
            {
                field.ShowMonthOf(new DateTime(2020, 8, 1));
                Assert.Equal("Ago", Header(field.MonthButton));
                TextBlock[] texts = (field.CalendarRoot as Border).Child.GetLogicalDescendantsOfTextBlock().ToArray();
                Assert.Contains(texts, text => text.Text == "SEG");
            }
            finally
            {
                window.Close();
                LocalizationService.Load("en");
            }
        }
    }

    internal static class TextBlockTree
    {
        public static System.Collections.Generic.IEnumerable<TextBlock> GetLogicalDescendantsOfTextBlock(this Control root)
        {
            if (root is TextBlock text)
            {
                yield return text;
            }

            if (root is Panel panel)
            {
                foreach (Control child in panel.Children)
                {
                    foreach (TextBlock found in child.GetLogicalDescendantsOfTextBlock())
                    {
                        yield return found;
                    }
                }
            }

            if (root is ContentControl { Content: Control content })
            {
                foreach (TextBlock found in content.GetLogicalDescendantsOfTextBlock())
                {
                    yield return found;
                }
            }
        }
    }
}
