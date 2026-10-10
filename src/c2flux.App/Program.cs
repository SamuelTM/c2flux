using System;
using Avalonia;

namespace c2flux
{
    public static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            FileIdentities.Reader =
                OperatingSystem.IsWindows() ? WindowsFileIdentity.TryRead
                : OperatingSystem.IsMacOS() ? MacFileIdentity.TryRead
                : LinuxFileIdentity.TryRead;

            RegisterPlatformServices();

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        // The native pieces Core and the UI ask for (tests use them too).
        public static void RegisterPlatformServices()
        {
            if (OperatingSystem.IsWindows())
            {
                Volumes.ClusterSizeReader = WindowsClusterSize.Read;
                FileIcons.Reader = WindowsFileIcons.Read;
            }
            else if (OperatingSystem.IsMacOS())
            {
                FileIcons.Reader = MacFileIcons.Read;
            }
        }

        // Also used by the XAML previewer.
        public static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .UseAppFonts()
                .LogToTrace();
        }
    }
}
