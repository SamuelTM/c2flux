using System.Globalization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace c2flux
{
    // Port of the WinForms AppDialogs: fixed-size message boxes with the
    // Windows warning or question icon, the default button highlighted.
    public static class AppDialogs
    {
        public static Task ShowWarningOkAsync(Window owner, string message, string title = null, string okText = null)
        {
            return ShowOkAsync(owner, DialogIconKind.Warning, message, title, okText);
        }

        // MessageBox.Show with MessageBoxIcon.Information.
        public static Task ShowInfoOkAsync(Window owner, string message, string title = null)
        {
            return ShowOkAsync(owner, DialogIconKind.Information, message, title, null);
        }

        // MessageBox.Show with MessageBoxIcon.Error.
        public static Task ShowErrorOkAsync(Window owner, string message, string title = null)
        {
            return ShowOkAsync(owner, DialogIconKind.Error, message, title ?? LocalizationService.GetText("Common.Error"), null);
        }

        private static Task ShowOkAsync(Window owner, DialogIconKind icon, string message, string title, string okText)
        {
            Window dialog = CreateDialog(title, 430, 178);
            Canvas canvas = (Canvas)dialog.Content;
            Button ok = CreateButton(okText ?? LocalizationService.GetText("Common.OK"), primary: true);

            Place(canvas, new DialogIcon { Kind = icon }, 28, 42, 32, 32);
            Place(canvas, CreateMessage(message), 82, 28, 324, 60);
            Place(canvas, ok, 326, 122, 84, 32);
            ok.IsDefault = true;
            ok.IsCancel = true;
            ok.Click += (_, _) => dialog.Close();
            return Show(dialog, owner);
        }

        // True for Yes. No is the highlighted (and Escape) button.
        public static Task<bool> ShowWarningYesNoAsync(Window owner, string message, string title = null, string yesText = null, string noText = null)
        {
            return ShowChoiceAsync(owner, DialogIconKind.Warning, message, title, yesText ?? LocalizationService.GetText("Common.Yes"), noText ?? LocalizationService.GetText("Common.No"));
        }

        // The MessageBox.Show calls of WinForms (YesNo with Question,
        // RetryCancel with Warning): true for the first button.
        public static Task<bool> ShowQuestionYesNoAsync(Window owner, string message, string title = null)
        {
            return ShowChoiceAsync(owner, DialogIconKind.Question, message, title, LocalizationService.GetText("Common.Yes"), LocalizationService.GetText("Common.No"));
        }

        public static Task<bool> ShowWarningRetryCancelAsync(Window owner, string message, string title = null)
        {
            return ShowChoiceAsync(owner, DialogIconKind.Warning, message, title, LocalizationService.GetText("Common.Retry"), LocalizationService.GetText("Common.Cancel"));
        }

        private static async Task<bool> ShowChoiceAsync(Window owner, DialogIconKind icon, string message, string title, string acceptText, string cancelText)
        {
            Window dialog = CreateDialog(title, 430, 178);
            Canvas canvas = (Canvas)dialog.Content;
            Button accept = CreateButton(acceptText, primary: false);
            Button cancel = CreateButton(cancelText, primary: true);
            bool result = false;

            Place(canvas, new DialogIcon { Kind = icon }, 28, 42, 32, 32);
            Place(canvas, CreateMessage(message), 82, 28, 324, 60);
            Place(canvas, accept, 232, 122, 84, 32);
            Place(canvas, cancel, 326, 122, 84, 32);
            accept.IsDefault = true;
            cancel.IsCancel = true;
            accept.Click += (_, _) => { result = true; dialog.Close(); };
            cancel.Click += (_, _) => dialog.Close();
            await Show(dialog, owner);
            return result;
        }

        public static async Task<(bool Restart, bool DoNotShowAgain)> ShowElevationPromptAsync(Window owner)
        {
            Window dialog = CreateDialog(AppConstants.ApplicationName, 480, 250);
            Canvas canvas = (Canvas)dialog.Content;
            TextBlock important = CreateMessage(LocalizationService.GetText("Elevation.Important"));
            important.FontWeight = FontWeight.Bold;
            // Gold on dark, red on light, as WinForms.
            important.Foreground = Application.Current?.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Light ? Brushes.Red : Brushes.Gold;
            CheckBox doNotShowAgain = new CheckBox { Content = LocalizationService.GetText("Elevation.DoNotShowAgain") };
            Button yes = CreateButton(LocalizationService.GetText("Common.Yes"), primary: false);
            Button no = CreateButton(LocalizationService.GetText("Common.No"), primary: true);
            bool restart = false;

            Place(canvas, new DialogIcon { Kind = DialogIconKind.Question }, 24, 37, 32, 32);
            Place(canvas, CreateMessage(LocalizationService.Format("Elevation.Message", AppConstants.ApplicationName)), 78, 20, 378, 64);
            Place(canvas, important, 78, 94, 378, 64);
            Place(canvas, doNotShowAgain, 24, 170, 300, 28);
            Place(canvas, yes, 294, 208, 84, 32);
            Place(canvas, no, 386, 208, 84, 32);
            yes.IsDefault = true;
            no.IsCancel = true;
            yes.Click += (_, _) => { restart = true; dialog.Close(); };
            no.Click += (_, _) => dialog.Close();
            await Show(dialog, owner);
            return (restart, doNotShowAgain.IsChecked == true);
        }

        private static Window CreateDialog(string title, double width, double height)
        {
            return new Window
            {
                Title = title ?? AppConstants.ApplicationName,
                Width = width,
                Height = height,
                CanResize = false,
                CanMinimize = false,
                CanMaximize = false,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new Canvas(),
            };
        }

        private static Task Show(Window dialog, Window owner)
        {
            if (owner != null)
            {
                return dialog.ShowDialog(owner);
            }

            // No window yet (startup): a plain window, centered on screen.
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            TaskCompletionSource closed = new TaskCompletionSource();
            dialog.Closed += (_, _) => closed.TrySetResult();
            dialog.Show();
            return closed.Task;
        }

        // AntdUI.Label with MiddleLeft alignment.
        private static TextBlock CreateMessage(string text)
        {
            return new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        }

        private static Button CreateButton(string text, bool primary)
        {
            Button button = new Button
            {
                Content = text,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Classes = { "ant", "dialog" },
            };

            if (primary)
            {
                button.Classes.Add("primary");
            }

            return button;
        }

        // Absolute placement; text is centered vertically in its box.
        private static void Place(Canvas canvas, Control control, double x, double y, double width, double height)
        {
            Canvas.SetLeft(control, x);
            Canvas.SetTop(control, y);
            control.Width = width;

            if (control is TextBlock text)
            {
                Border box = new Border { Width = width, Height = height, Child = text };
                Canvas.SetLeft(box, x);
                Canvas.SetTop(box, y);
                canvas.Children.Add(box);
                return;
            }

            control.Height = height;
            canvas.Children.Add(control);
        }
    }

    public enum DialogIconKind
    {
        Warning,
        Question,
        Error,
        Information,
    }

    // The Windows message box icons (IDI_WARNING, IDI_QUESTION, IDI_ERROR) at 32 px,
    // drawn so they look the same on every OS.
    public sealed class DialogIcon : Control
    {
        public DialogIconKind Kind { get; set; }

        public override void Render(DrawingContext context)
        {
            if (Kind == DialogIconKind.Warning)
            {
                StreamGeometry triangle = new StreamGeometry();

                using (StreamGeometryContext path = triangle.Open())
                {
                    path.BeginFigure(new Point(16, 2), true);
                    path.LineTo(new Point(31, 29));
                    path.LineTo(new Point(1, 29));
                    path.EndFigure(true);
                }

                LinearGradientBrush fill = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Color.FromRgb(255, 222, 89), 0), new GradientStop(Color.FromRgb(243, 175, 0), 1) },
                };
                context.DrawGeometry(fill, new Pen(new SolidColorBrush(Color.FromRgb(160, 110, 0)), 1, lineJoin: PenLineJoin.Round), triangle);
                DrawGlyph(context, "!", Brushes.Black, 20, 2);
            }
            else if (Kind == DialogIconKind.Information)
            {
                context.DrawEllipse(new SolidColorBrush(Color.FromRgb(0, 103, 192)), new Pen(new SolidColorBrush(Color.FromRgb(0, 70, 140)), 1), new Rect(1, 1, 30, 30));
                DrawGlyph(context, "i", Brushes.White, 20, 0);
            }
            else if (Kind == DialogIconKind.Error)
            {
                context.DrawEllipse(new SolidColorBrush(Color.FromRgb(196, 43, 28)), new Pen(new SolidColorBrush(Color.FromRgb(135, 24, 15)), 1), new Rect(1, 1, 30, 30));
                Pen cross = new Pen(Brushes.White, 3, lineCap: PenLineCap.Round);
                context.DrawLine(cross, new Point(11, 11), new Point(21, 21));
                context.DrawLine(cross, new Point(21, 11), new Point(11, 21));
            }
            else
            {
                RadialGradientBrush fill = new RadialGradientBrush
                {
                    Center = new RelativePoint(0.4, 0.3, RelativeUnit.Relative),
                    GradientOrigin = new RelativePoint(0.4, 0.3, RelativeUnit.Relative),
                    RadiusX = new RelativeScalar(0.75, RelativeUnit.Relative),
                    RadiusY = new RelativeScalar(0.75, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Color.FromRgb(96, 160, 240), 0), new GradientStop(Color.FromRgb(16, 72, 168), 1) },
                };
                context.DrawEllipse(fill, new Pen(new SolidColorBrush(Color.FromRgb(10, 50, 120)), 1), new Rect(1, 1, 30, 30));
                DrawGlyph(context, "?", Brushes.White, 20, 0);
            }
        }

        private void DrawGlyph(DrawingContext context, string glyph, IBrush brush, double size, double offsetY)
        {
            FormattedText text = new FormattedText(glyph, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold), size, brush);
            context.DrawText(text, new Point((32 - text.Width) / 2, (32 - text.Height) / 2 + offsetY));
        }
    }
}
