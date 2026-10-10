using System;
using System.IO;
using Xunit;

namespace c2flux.Core.Tests
{
    public class FileIdentitiesTests
    {
        // The reader of the OS running the tests (Windows has no hard link
        // API in .NET to set this up, so it only checks the default there).
        private static FileIdentityReader NativeReader()
        {
            if (OperatingSystem.IsMacOS()) return MacFileIdentity.TryRead;
            if (OperatingSystem.IsLinux()) return LinuxFileIdentity.TryRead;
            return null;
        }

        [Fact]
        public void Hard_links_share_an_identity_and_copies_do_not()
        {
            FileIdentityReader reader = NativeReader();

            if (reader == null)
            {
                return;
            }

            string directory = TestEnvironment.CreateDirectory("identity");
            string original = Path.Combine(directory, "original.bin");
            string link = Path.Combine(directory, "link.bin");
            string copy = Path.Combine(directory, "copy.bin");
            File.WriteAllBytes(original, new byte[] { 1, 2, 3 });
            File.Copy(original, copy);
            Assert.Equal(0, link_(original, link));

            Assert.True(reader(original, out FileIdentity originalIdentity, out _, out bool hasUsn));
            Assert.True(reader(link, out FileIdentity linkIdentity, out _, out _));
            Assert.True(reader(copy, out FileIdentity copyIdentity, out _, out _));

            Assert.False(hasUsn);
            Assert.Equal(originalIdentity, linkIdentity);
            Assert.NotEqual(originalIdentity, copyIdentity);
            Assert.False(reader(Path.Combine(directory, "missing.bin"), out _, out _, out _));
        }

        [Fact]
        public void The_default_reader_tells_paths_apart()
        {
            string directory = TestEnvironment.CreateDirectory("identity-default");
            string first = Path.Combine(directory, "a.bin");
            string second = Path.Combine(directory, "b.bin");
            File.WriteAllBytes(first, new byte[] { 1 });
            File.WriteAllBytes(second, new byte[] { 1 });

            Assert.True(FileIdentities.ByPath(first, out FileIdentity a1, out _, out _));
            Assert.True(FileIdentities.ByPath(first, out FileIdentity a2, out _, out _));
            Assert.True(FileIdentities.ByPath(second, out FileIdentity b, out _, out _));

            Assert.Equal(a1, a2);
            Assert.NotEqual(a1, b);
            Assert.False(FileIdentities.ByPath(Path.Combine(directory, "missing.bin"), out _, out _, out _));
        }

        [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "link", SetLastError = true)]
        private static extern int link_(string existing, string created);
    }
}
