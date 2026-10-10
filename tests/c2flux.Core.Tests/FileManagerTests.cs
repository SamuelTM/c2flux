using System.Linq;
using Xunit;

namespace c2flux.Core.Tests
{
    public class FileManagerTests
    {
        [Fact]
        public void Windows_selects_the_item_in_Explorer()
        {
            var command = FileManager.RevealCommands(AppPaths.Platform.Windows, @"C:\Data\a b.txt").Single();

            Assert.Equal("explorer.exe", command.FileName);
            Assert.Equal(new[] { @"/select,C:\Data\a b.txt" }, command.Arguments);
        }

        [Fact]
        public void MacOS_reveals_the_item_in_Finder()
        {
            var command = FileManager.RevealCommands(AppPaths.Platform.MacOS, "/Users/me/a b.txt").Single();

            Assert.Equal("open", command.FileName);
            Assert.Equal(new[] { "-R", "/Users/me/a b.txt" }, command.Arguments);
        }

        [Fact]
        public void Linux_asks_the_file_manager_over_DBus_then_opens_the_folder()
        {
            var commands = FileManager.RevealCommands(AppPaths.Platform.Linux, "/home/me/a b.txt").ToArray();

            Assert.Equal("dbus-send", commands[0].FileName);
            Assert.Contains("array:string:file:///home/me/a%20b.txt", commands[0].Arguments);
            Assert.Equal("xdg-open", commands[1].FileName);
            Assert.Equal(new[] { "/home/me" }, commands[1].Arguments);
        }
    }
}
