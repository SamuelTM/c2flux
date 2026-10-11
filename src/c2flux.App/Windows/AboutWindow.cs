using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace c2flux
{
    // Port of the WinForms AboutForm: the cat, name, copyright, version, the
    // GitHub update status and links, and the Ko-fi button.
    public sealed class AboutWindow : Window
    {
        // LinkLabel colors on dark and light backgrounds.
        private static readonly IBrush DarkLink = new SolidColorBrush(Color.FromRgb(140, 200, 255));
        private static readonly IBrush LightLink = new SolidColorBrush(Color.FromRgb(0, 102, 204));

        private readonly TextBlock _update;

        public AboutWindow(AppSettings settings)
        {
            Title = LocalizationService.Format("About.Title", AppConstants.ApplicationName);
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://c2flux.App/Assets/c2flux.png")));
            Width = 475;
            Height = 309;
            CanResize = false;
            CanMinimize = false;
            CanMaximize = false;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            Canvas canvas = new Canvas();
            Place(canvas, CreateCatImage(), 20, 24);
            // Bold in the WinForms code, but the theme resets the font: regular.
            Place(canvas, new TextBlock { Text = AppConstants.FullApplicationName }, 122, 26);
            Place(canvas, new TextBlock { Text = AppConstants.CopyrightText }, 122, 58);
            Place(canvas, new TextBlock { Text = LocalizationService.GetText("About.VersionPrefix") + GitHubUpdateService.GetApplicationVersionText() }, 122, 82);
            _update = Link(LocalizationService.GetText("About.UpdateChecking"), null);
            Place(canvas, _update, 122, 106);
            Place(canvas, Link(AppConstants.GitHubRepositoryUrl, AppConstants.GitHubRepositoryUrl), 122, 130);
            Place(canvas, Link("Help: " + AppConstants.HelpUrl, AppConstants.HelpUrl), 122, 154);
            Place(canvas, new TextBlock
            {
                Text = LocalizationService.Format("About.FreeText", AppConstants.ApplicationName) + Environment.NewLine + LocalizationService.GetText("About.SupportText"),
                Width = 435,
                TextWrapping = TextWrapping.Wrap,
            }, 20, 194);

            Image koFi = new Image { Source = LoadKoFi(), Width = 179, Height = 42, Cursor = new Cursor(StandardCursorType.Hand) };
            koFi.PointerPressed += (_, _) => FileManager.Open(AppConstants.KoFiUrl);
            Place(canvas, koFi, 20, 244);

            Button ok = new Button { Content = LocalizationService.GetText("Common.OK"), Width = 90, Height = 32, HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center, Classes = { "ant", "dialog" }, IsDefault = true, IsCancel = true };
            ok.Click += (_, _) => Close();
            Place(canvas, ok, 365, 254);
            Content = canvas;

            if (settings.AutoCheckForUpdates)
            {
                Opened += async (_, _) => await UpdateGitHubStatusAsync();
            }
            else
            {
                _update.Text = LocalizationService.GetText("About.UpdateCheckDisabled");
                _update.IsEnabled = false;
                _update.Foreground = Brushes.Gray;
            }
        }

        private async Task UpdateGitHubStatusAsync()
        {
            try
            {
                GitHubUpdateResult result = await GitHubUpdateService.CheckForUpdateAsync();

                if (result.ErrorKind != GitHubUpdateErrorKind.None)
                {
                    _update.Text = result.ErrorKind is GitHubUpdateErrorKind.Timeout or GitHubUpdateErrorKind.Network or GitHubUpdateErrorKind.Http
                        ? LocalizationService.GetText("About.GitHubUnavailable")
                        : LocalizationService.GetText("Common.Error");
                }
                else if (!result.UpdateAvailable)
                {
                    _update.Text = LocalizationService.GetText("About.NoNewVersion");
                }
                else
                {
                    _update.Text = LocalizationService.Format("About.UpdateAvailable", result.LatestVersion);
                    _update.Tag = result.DownloadUrl;
                    _update.Cursor = new Cursor(StandardCursorType.Hand);
                }
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                AppAlertLog.AddWarning("GitHub update", "The About dialog could not update the GitHub status.", exception.ToString());
                _update.Text = LocalizationService.GetText("Common.Error");
            }
        }

        // A LinkLabel: link color, underlined while hovered; Tag holds the URL.
        private TextBlock Link(string text, string url)
        {
            TextBlock link = new TextBlock { Text = text, Tag = url };
            link.Foreground = ActualThemeVariant == Avalonia.Styling.ThemeVariant.Light ? LightLink : DarkLink;

            if (url != null)
            {
                link.Cursor = new Cursor(StandardCursorType.Hand);
            }

            link.PointerEntered += (_, _) => link.TextDecorations = link.Tag is string ? TextDecorations.Underline : null;
            link.PointerExited += (_, _) => link.TextDecorations = null;
            link.PointerPressed += (_, _) =>
            {
                if (link.Tag is string target && !string.IsNullOrWhiteSpace(target))
                {
                    FileManager.Open(target);
                }
            };
            return link;
        }

        // The photo in a 76 px circle with a 2 px steel blue ring, in 82 px.
        private static Control CreateCatImage()
        {
            Bitmap photo = new Bitmap(AssetLoader.Open(new Uri("avares://c2flux.App/Assets/molotov.jpg")));
            Panel panel = new Panel { Width = 82, Height = 82 };
            panel.Children.Add(new Image
            {
                Source = photo,
                Width = 76,
                Height = 76,
                Stretch = Stretch.UniformToFill,
                Margin = new Thickness(3),
                Clip = new EllipseGeometry(new Rect(0, 0, 76, 76)),
            });
            panel.Children.Add(new Avalonia.Controls.Shapes.Ellipse { Width = 78, Height = 78, Margin = new Thickness(2), Stroke = Brushes.SteelBlue, StrokeThickness = 2 });
            return panel;
        }

        // ko-fi.png has a white background; WinForms made it transparent.
        private static Bitmap LoadKoFi()
        {
            using Bitmap source = new Bitmap(AssetLoader.Open(new Uri("avares://c2flux.App/Assets/ko-fi.png")));
            PixelSize size = source.PixelSize;
            byte[] pixels = new byte[size.Width * size.Height * 4];
            GCHandle handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);

            try
            {
                source.CopyPixels(new PixelRect(size), handle.AddrOfPinnedObject(), pixels.Length, size.Width * 4);

                for (int index = 0; index < pixels.Length; index += 4)
                {
                    if (pixels[index] == 255 && pixels[index + 1] == 255 && pixels[index + 2] == 255)
                    {
                        pixels[index] = pixels[index + 1] = pixels[index + 2] = pixels[index + 3] = 0;
                    }
                }

                // Same channel order as the decoded source (RGBA on some OSes).
                return new Bitmap(source.Format ?? PixelFormat.Bgra8888, AlphaFormat.Premul, handle.AddrOfPinnedObject(), size, new Vector(96, 96), size.Width * 4);
            }
            finally
            {
                handle.Free();
            }
        }

        private static void Place(Canvas canvas, Control control, double x, double y)
        {
            Canvas.SetLeft(control, x);
            Canvas.SetTop(control, y);
            canvas.Children.Add(control);
        }
    }

    // Port of the WinForms UpdateAvailableForm: the new version, its release
    // notes and Download (default), Later, Changelog.
    public sealed class UpdateAvailableWindow : Window
    {
        public UpdateAvailableWindow(GitHubUpdateResult result)
        {
            bool hasNotes = !string.IsNullOrWhiteSpace(result.ReleaseNotes);
            Title = AppConstants.ApplicationName;
            Width = hasNotes ? 520 : 420;
            Height = hasNotes ? 260 : 176;
            CanResize = false;
            CanMinimize = false;
            CanMaximize = false;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            Canvas canvas = new Canvas();
            AddAt(canvas, new DialogIcon { Kind = DialogIconKind.Warning, Width = 32, Height = 32 }, 38, 34);
            AddAt(canvas, new TextBlock
            {
                Text = LocalizationService.Format("About.UpdateAvailableMessage", AppConstants.ApplicationName, result.LatestVersion ?? string.Empty),
                Width = 324,
                Height = 48,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Padding = new Thickness(0, 15, 0, 0),
            }, 80, 24);

            if (hasNotes)
            {
                AddAt(canvas, new TextBlock { Text = result.ReleaseNotes, Width = 420, Height = 116, TextWrapping = TextWrapping.Wrap }, 80, 80);
            }

            Button download = Button("About.UpdateDownload", primary: true);
            Button later = Button("About.UpdateLater", primary: false);
            Button changelog = Button("About.UpdateChangelog", primary: false);
            download.IsDefault = true;
            later.IsCancel = true;
            download.Click += (_, _) =>
            {
                Open(result.DownloadUrl);
                Close();
            };
            later.Click += (_, _) => Close();
            changelog.Click += (_, _) => Open(result.DownloadUrl);

            double top = Height - 23 - 32;
            double right = Width - 20;
            AddAt(canvas, changelog, right - 90, top);
            AddAt(canvas, later, right - 90 * 2 - 8, top);
            AddAt(canvas, download, right - 90 * 3 - 16, top);
            Content = canvas;
        }

        private static void Open(string url)
        {
            if (!string.IsNullOrWhiteSpace(url))
            {
                FileManager.Open(url);
            }
        }

        private static Button Button(string textKey, bool primary)
        {
            Button button = new Button
            {
                Content = LocalizationService.GetText(textKey),
                Width = 90,
                Height = 32,
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                Classes = { "ant", "dialog" },
            };

            if (primary)
            {
                button.Classes.Add("primary");
            }

            return button;
        }

        private static void AddAt(Canvas canvas, Control control, double x, double y)
        {
            Canvas.SetLeft(control, x);
            Canvas.SetTop(control, y);
            canvas.Children.Add(control);
        }
    }
}
