using Xunit;

namespace c2flux.Core.Tests
{
    public class FileTypeCategoriesTests
    {
        [Theory]
        [InlineData("photo.JPG", "Advanced.FileType.Images")]
        [InlineData("vzdump-qemu-100.vma.zst", "Advanced.FileType.Backups")]
        [InlineData("data.012", "Advanced.FileType.GameFiles")]
        [InlineData("enshrouded_007.dat", "Advanced.FileType.GameFiles")]
        [InlineData("readme", "Advanced.FileType.OtherFiles")]
        public void Names_map_to_their_category(string fileName, string expected)
        {
            Assert.Equal(expected, FileTypeCategories.GetCategoryKey(fileName));
        }
    }
}
