using System;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Headless;
using c2flux.AppTests;

[assembly: AvaloniaTestApplication(typeof(TestEnvironment))]
// Language and theme are global state.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace c2flux.AppTests
{
    public static class TestEnvironment
    {
        // Settings and languages go to a throwaway directory, not the
        // developer's real c2flux data.
        [ModuleInitializer]
        internal static void Initialize()
        {
            string home = Path.Combine(Path.GetTempPath(), "c2flux-app-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(home);
            Environment.SetEnvironmentVariable(AppPaths.HomeVariable, home);
            // Dates in UTC and numbers in en-US, like the Windows runner of
            // the reference captures (TZ is read on macOS and Linux).
            Environment.SetEnvironmentVariable("TZ", "UTC");
            TimeZoneInfo.ClearCachedData();
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        }

        public static AppBuilder BuildAvaloniaApp()
        {
            // Real Skia drawing and the app's font, so chart captures look
            // like the app.
            return AppBuilder.Configure<App>()
                .UseSkia()
                .UseAppFonts()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
        }
    }
}
