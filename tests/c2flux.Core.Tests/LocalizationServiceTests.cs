using Xunit;

namespace c2flux.Core.Tests
{
    public class LocalizationServiceTests
    {
        [Theory]
        [InlineData("pt", "pt")]
        [InlineData("PT-br", "pt")]
        [InlineData("pt_BR", "pt")]
        [InlineData("en", "en")]
        // Languages of older versions, in saved settings.
        [InlineData("de", "en")]
        [InlineData("ptx", "en")]
        [InlineData(null, "en")]
        public void Only_english_and_portuguese_exist(string code, string expected)
        {
            Assert.Equal(expected, LocalizationService.NormalizeLanguageCode(code));
        }
    }
}
