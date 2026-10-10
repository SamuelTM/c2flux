using System.Linq;
using Xunit;

namespace c2flux.AppTests
{
    public class ExportTests
    {
        private static FileSystemEntry Tree()
        {
            FileSystemEntry root = new FileSystemEntry { Name = "root", IsDirectory = true, SizeBytes = 3 * 1024 * 1024 + 10 };
            FileSystemEntry sub = new FileSystemEntry { Name = "sub", IsDirectory = true, SizeBytes = 3 * 1024 * 1024 };
            sub.Children.Add(new FileSystemEntry { Name = "big.bin", SizeBytes = 3 * 1024 * 1024 });
            root.Children.Add(sub);
            root.Children.Add(new FileSystemEntry { Name = "a.txt", SizeBytes = 10 });
            return root;
        }

        [Fact]
        public void Tree_text_draws_branches_with_sizes()
        {
            Assert.Equal(
                "root [3 MB]\n├─ sub [3 MB]\n│  └─ big.bin [3 MB]\n└─ a.txt [10 B]",
                ExportActions.BuildTreeText(Tree(), null));
        }

        [Fact]
        public void Tree_text_stops_at_the_export_depth()
        {
            Assert.Equal("root [3 MB]\n├─ sub [3 MB]\n└─ a.txt [10 B]", ExportActions.BuildTreeText(Tree(), 1));
        }

        [Fact]
        public void WinForms_file_filters_become_picker_types()
        {
            var types = FileDialogs.ParseFilter("WTF Scan (*.wtfscan;*.json)|*.wtfscan;*.json|All (*.*)|*.*");

            Assert.Equal(new[] { "WTF Scan (*.wtfscan;*.json)", "All (*.*)" }, types.Select(type => type.Name));
            Assert.Equal(new[] { "*.wtfscan", "*.json" }, types[0].Patterns);
        }
    }
}
