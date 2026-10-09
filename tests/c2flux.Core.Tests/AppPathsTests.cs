using System.Collections.Generic;
using System.IO;
using Xunit;

namespace c2flux.Core.Tests
{
    public class AppPathsTests
    {
        // Built with Path.Combine so the expectations hold on every OS: the
        // rules are tested here, not the platform's path syntax.
        private static readonly string BaseDirectory = Path.Combine(Path.GetTempPath(), "app") + Path.DirectorySeparatorChar;
        private static readonly string AppDirectory = Path.Combine(Path.GetTempPath(), "app");
        private static readonly string Profile = Path.Combine(Path.GetTempPath(), "home", "user");
        private static readonly string LocalAppData = Path.Combine(Profile, "AppData", "Local");

        private static AppPaths.AppPathSet Resolve(AppPaths.Platform platform, Dictionary<string, string> environment = null)
        {
            environment ??= new Dictionary<string, string>();
            return AppPaths.Resolve(
                platform,
                name => environment.TryGetValue(name, out string value) ? value : null,
                BaseDirectory,
                Profile,
                LocalAppData);
        }

        [Fact]
        public void Windows_keeps_everything_next_to_the_executable_as_in_v141()
        {
            AppPaths.AppPathSet paths = Resolve(AppPaths.Platform.Windows);

            Assert.Equal(AppDirectory, paths.Resources);
            Assert.Equal(AppDirectory, paths.Config);
            Assert.Equal(AppDirectory, paths.Data);
            Assert.Equal(AppDirectory, paths.Cache);
            Assert.Equal(Path.Combine(LocalAppData, "WTF", "ScanCache"), paths.ScanCache);

            // The exact file the v1.4.1 app used: AppContext.BaseDirectory + Settings.
            Assert.Equal(
                Path.Combine(BaseDirectory, "Settings", "settings.json"),
                Path.Combine(paths.Config, "Settings", "settings.json"));
        }

        [Fact]
        public void MacOS_uses_application_support_and_caches()
        {
            AppPaths.AppPathSet paths = Resolve(AppPaths.Platform.MacOS);

            string support = Path.Combine(Profile, "Library", "Application Support", "c2flux");
            string caches = Path.Combine(Profile, "Library", "Caches", "c2flux");

            Assert.Equal(AppDirectory, paths.Resources);
            Assert.Equal(support, paths.Config);
            Assert.Equal(support, paths.Data);
            Assert.Equal(caches, paths.Cache);
            Assert.Equal(Path.Combine(caches, "ScanCache"), paths.ScanCache);
        }

        [Fact]
        public void Linux_uses_xdg_defaults()
        {
            AppPaths.AppPathSet paths = Resolve(AppPaths.Platform.Linux);

            Assert.Equal(Path.Combine(Profile, ".config", "c2flux"), paths.Config);
            Assert.Equal(Path.Combine(Profile, ".local", "share", "c2flux"), paths.Data);
            Assert.Equal(Path.Combine(Profile, ".cache", "c2flux"), paths.Cache);
            Assert.Equal(Path.Combine(Profile, ".cache", "c2flux", "ScanCache"), paths.ScanCache);
        }

        [Fact]
        public void Linux_honors_absolute_xdg_variables_and_ignores_relative_ones()
        {
            string config = Path.Combine(Path.GetTempPath(), "xdg-config");

            AppPaths.AppPathSet paths = Resolve(AppPaths.Platform.Linux, new Dictionary<string, string>
            {
                ["XDG_CONFIG_HOME"] = config,
                ["XDG_DATA_HOME"] = "relative/data",
            });

            Assert.Equal(Path.Combine(config, "c2flux"), paths.Config);
            Assert.Equal(Path.Combine(Profile, ".local", "share", "c2flux"), paths.Data);
        }

        // The platform is passed by name: AppPaths.Platform is internal and
        // cannot appear in a public test method's signature.
        [Theory]
        [InlineData("Windows")]
        [InlineData("MacOS")]
        [InlineData("Linux")]
        public void C2fluxHome_puts_everything_in_one_directory(string platform)
        {
            string home = Path.Combine(Path.GetTempPath(), "portable-c2flux");

            AppPaths.AppPathSet paths = Resolve(System.Enum.Parse<AppPaths.Platform>(platform), new Dictionary<string, string>
            {
                [AppPaths.HomeVariable] = home,
            });

            Assert.Equal(AppDirectory, paths.Resources);
            Assert.Equal(home, paths.Config);
            Assert.Equal(home, paths.Data);
            Assert.Equal(home, paths.Cache);
            Assert.Equal(Path.Combine(home, "ScanCache"), paths.ScanCache);
        }
    }
}
