using System.IO;
using Xunit;

namespace c2flux.Core.Tests
{
    public class AppSettingsTests
    {
        [Fact]
        public void Settings_are_saved_under_the_config_directory_and_load_back()
        {
            AppSettings settings = AppSettings.Load();
            settings.LanguageCode = "pt";
            settings.SunburstDepth = 7;
            settings.Save();

            Assert.True(File.Exists(Path.Combine(AppPaths.ConfigDirectory, "Settings", "settings.json")));

            AppSettings loaded = AppSettings.Load();
            Assert.Equal("pt", loaded.LanguageCode);
            Assert.Equal(7, loaded.SunburstDepth);
        }
    }
}
