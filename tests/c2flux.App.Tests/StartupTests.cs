using System.IO;
using Xunit;

namespace c2flux.AppTests
{
    public class StartupTests
    {
        // How a relaunch through UAC passes each argument (CommandLineToArgvW).
        [Theory]
        [InlineData("plain", "\"plain\"")]
        [InlineData(@"C:\Program Files\", @"""C:\Program Files\\""")]
        [InlineData("say \"hi\"", @"""say \""hi\""""")]
        [InlineData(@"a\""b", @"""a\\\""b""")]
        public void Elevated_restart_quotes_arguments(string argument, string expected)
        {
            Assert.Equal(expected, WindowsElevation.Quote(argument));
        }

        [Fact]
        public void The_first_argument_is_scanned_when_it_is_a_folder()
        {
            string folder = Path.GetTempPath();

            Assert.Equal(folder, App.GetStartupScanPath(new[] { folder }));
            Assert.Null(App.GetStartupScanPath(new[] { "--search", folder }));
            Assert.Equal(folder, App.GetStartupSearchPath(new[] { "--search", folder }));
            Assert.Null(App.GetStartupSearchPath(new[] { folder }));
            Assert.Null(App.GetStartupScanPath(new[] { Path.Combine(folder, "c2flux-no-such-folder") }));
            Assert.Null(App.GetStartupScanPath(new string[0]));
        }
    }
}
