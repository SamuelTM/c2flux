using System.Runtime.InteropServices;
using System.Text.Json;
using Xunit;

namespace c2flux.Core.Tests
{
    public class GitHubUpdateServiceTests
    {
        [Theory]
        [InlineData("Windows", Architecture.X64, "-windows-x64.zip")]
        [InlineData("MacOS", Architecture.Arm64, "-macos-arm64.dmg")]
        [InlineData("Linux", Architecture.Arm64, "-linux-aarch64.AppImage")]
        [InlineData("Linux", Architecture.X64, "-linux-x86_64.AppImage")]
        public void Package_suffix_names_this_build(string platform, Architecture architecture, string expected)
        {
            Assert.Equal(expected, GitHubUpdateService.GetPackageSuffix(System.Enum.Parse<AppPaths.Platform>(platform), architecture));
        }

        [Fact]
        public void Download_points_at_the_matching_asset_only()
        {
            JsonElement release = JsonDocument.Parse(@"{""assets"":[
                {""name"":""c2flux-v2.0.0-win-x64.zip"",""browser_download_url"":""https://x/win.zip""},
                {""name"":""c2flux-v2.0.0-macos-arm64.dmg"",""browser_download_url"":""https://x/mac.dmg""}]}").RootElement;

            Assert.Equal("https://x/mac.dmg", GitHubUpdateService.FindAssetUrl(release, "-macos-arm64.dmg"));
            Assert.Null(GitHubUpdateService.FindAssetUrl(release, "-windows-x64.zip"));
            Assert.Null(GitHubUpdateService.FindAssetUrl(release, null));
        }
    }
}
