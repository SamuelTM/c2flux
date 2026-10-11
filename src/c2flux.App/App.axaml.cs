using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;

namespace c2flux
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        // As the WinForms Program.Main: settings, log, language, elevation,
        // then the main window (optionally scanning the folder given as the
        // first argument).
        public override void OnFrameworkInitializationCompleted()
        {
            AppSettings settings = AppSettings.Load();
            AppAlertLog.Configure(settings.LogLevel, settings.AutoSaveLog, settings.MaximumLogFileSizeMb);
            LocalizationService.Initialize(settings.LanguageCode);
            ApplyLayout(settings.Layout);

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                string[] args = desktop.Args ?? Array.Empty<string>();

                if (OperatingSystem.IsWindows() && settings.StartElevatedOnStartup && !WindowsElevation.IsElevated() && WindowsElevation.TryRestartElevated(args))
                {
                    desktop.Shutdown();
                    base.OnFrameworkInitializationCompleted();
                    return;
                }

                MainWindow main = new MainWindow(settings);
                desktop.MainWindow = main;

                // Core services warn through the UI.
                AppNotifications.WarningHandler = (_, message, title, okText) =>
                    Dispatcher.UIThread.Post(async () => await AppDialogs.ShowWarningOkAsync(main.IsVisible ? main : null, message, title, okText));

                main.Opened += async (_, _) =>
                {
                    foreach (string warning in new[] { AppSettings.StartupWarningMessage, LocalizationService.StartupWarningMessage }.Where(text => !string.IsNullOrWhiteSpace(text)))
                    {
                        await AppDialogs.ShowWarningOkAsync(main, warning);
                    }

                    if (OperatingSystem.IsWindows() && settings.ShowElevationPromptOnStartup && !WindowsElevation.IsElevated())
                    {
                        (bool restart, bool doNotShowAgain) = await AppDialogs.ShowElevationPromptAsync(main);

                        if (doNotShowAgain)
                        {
                            settings.ShowElevationPromptOnStartup = false;
                            settings.Save();
                        }

                        if (restart && WindowsElevation.TryRestartElevated(args))
                        {
                            main.Close();
                            return;
                        }
                    }

                    if (settings.AutoCheckForUpdates)
                    {
                        GitHubUpdateResult update = await GitHubUpdateService.CheckForUpdateAsync();

                        if (update.CanConnectToGitHub && update.UpdateAvailable)
                        {
                            await new UpdateAvailableWindow(update).ShowDialog(main);
                        }
                    }

                    string startupPath = GetStartupScanPath(args);

                    if (startupPath != null)
                    {
                        await main.ScanPathAsync(startupPath);
                    }
                };
            }

            base.OnFrameworkInitializationCompleted();
        }

        private async void OnAboutClick(object sender, EventArgs e)
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: MainWindow main })
            {
                await main.ShowAboutAsync();
            }
        }

        // The folder to scan at startup: the first argument (not "--search"),
        // "C:" completed to "C:\", only when it exists.
        internal static string GetStartupScanPath(string[] args)
        {
            if (args.Length == 0 || string.Equals(args[0], "--search", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string path = args[0].Trim().Trim('"');

            if (path.Length == 2 && path[1] == ':')
            {
                path += "\\";
            }

            return Directory.Exists(path) ? path : null;
        }

        // WindowsDefault follows the OS (Windows, macOS and Linux all report it).
        public static void ApplyLayout(AppLayout layout)
        {
            Current.RequestedThemeVariant = layout switch
            {
                AppLayout.WindowsLightMode => ThemeVariant.Light,
                AppLayout.WindowsDarkMode => ThemeVariant.Dark,
                _ => ThemeVariant.Default,
            };
        }
    }
}
