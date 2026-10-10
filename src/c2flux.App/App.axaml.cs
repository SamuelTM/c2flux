using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace c2flux
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            AppSettings settings = AppSettings.Load();
            LocalizationService.Initialize(settings.LanguageCode);
            ApplyLayout(settings.Layout);

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow();
            }

            base.OnFrameworkInitializationCompleted();
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
