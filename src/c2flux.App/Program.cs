using System;
using Avalonia;

namespace c2flux
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            FileIdentities.Reader =
                OperatingSystem.IsWindows() ? WindowsFileIdentity.TryRead
                : OperatingSystem.IsMacOS() ? MacFileIdentity.TryRead
                : LinuxFileIdentity.TryRead;

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        // Also used by the XAML previewer.
        public static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
        }
    }
}
