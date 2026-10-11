using Xunit;

namespace c2flux.Core.Tests
{
    public class SearchServiceTests
    {
        [Fact]
        public void Mount_point_resolver_picks_the_longest_matching_root()
        {
            var drive = SearchService.CreateMountPointResolver(new[] { "/", "/Volumes/USB", "/Volumes/USB 2/" });

            Assert.Equal("/", drive("/Users/me/file.txt"));
            Assert.Equal("/Volumes/USB", drive("/Volumes/USB/a.bin"));
            Assert.Equal("/Volumes/USB 2", drive("/Volumes/USB 2/a.bin"));
            Assert.Equal("/", drive("/Volumes/USBX/a.bin"));
        }
    }
}
