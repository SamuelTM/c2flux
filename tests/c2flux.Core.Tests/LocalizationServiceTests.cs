using System.IO;
using Xunit;

namespace c2flux.Core.Tests
{
    // Languages ship next to the executable (read-only on macOS and Linux) and
    // users can install more into their data directory. Both must be listed,
    // and a user's file must win over a bundled one with the same code.
    //
    // The test project ships Languages/lang_zz-bundled.json and
    // lang_zz-both.json next to its own executable as "bundled" languages.
    public class LocalizationServiceTests
    {
        [Fact]
        public void Bundled_and_user_languages_are_both_available_and_user_files_win()
        {
            string userDirectory = LocalizationService.GetSettingsDirectoryPath();
            Directory.CreateDirectory(userDirectory);
            File.WriteAllText(Path.Combine(userDirectory, "lang_zz-user.json"), "{ \"Common.OK\": \"OK (user)\" }");
            File.WriteAllText(Path.Combine(userDirectory, "lang_zz-both.json"), "{ \"Common.OK\": \"OK (user override)\" }");

            string[] codes = LocalizationService.GetAvailableLanguageCodes();

            Assert.Contains("zz-bundled", codes);
            Assert.Contains("zz-user", codes);
            Assert.Contains("zz-both", codes);
            Assert.Single(codes, code => code == "zz-both");

            Assert.Equal(
                Path.Combine(LocalizationService.GetBundledLanguageDirectoryPath(), "lang_zz-bundled.json"),
                LocalizationService.ResolveLanguageFilePath("zz-bundled"));
            Assert.Equal(
                Path.Combine(userDirectory, "lang_zz-both.json"),
                LocalizationService.ResolveLanguageFilePath("zz-both"));
        }

        [Fact]
        public void Installed_languages_go_to_the_user_directory()
        {
            Assert.Equal(
                Path.Combine(AppPaths.DataDirectory, "Languages", "lang_pt.json"),
                LocalizationService.GetLanguageFilePath("pt"));
        }
    }
}
