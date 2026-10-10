using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Xunit;

namespace c2flux.AppTests
{
    public class ChartLogicTests
    {
        [Fact]
        public void Pie_keeps_the_ten_largest_and_sums_the_rest_as_other()
        {
            FileSystemEntry root = new FileSystemEntry { Name = "root", IsDirectory = true };
            root.Children.AddRange(Enumerable.Range(1, 12).Select(size => new FileSystemEntry { Name = "f" + size, SizeBytes = size }));
            root.Children.Add(new FileSystemEntry { Name = "empty", SizeBytes = 0 });

            List<PieChart.ChartItem> items = PieChart.CreateItems(root);

            Assert.Equal(11, items.Count);
            Assert.Equal(new long[] { 12, 11, 10, 9, 8, 7, 6, 5, 4, 3 }, items.Take(10).Select(item => item.SizeBytes));
            Assert.Null(items[10].Entry);
            Assert.Equal(1 + 2, items[10].SizeBytes);
        }

        [Theory]
        // Slice from 12 to 3 o'clock (GDI+ angles: -90 to 0).
        [InlineData(60, 10, true)]
        [InlineData(90, 40, true)]
        [InlineData(10, 60, false)]
        [InlineData(55, 45, true)]
        [InlineData(55, 55, false)]
        [InlineData(99, 1, false)]
        public void Pie_slice_hit_test_follows_gdi_angles(double x, double y, bool expected)
        {
            Rect bounds = new Rect(0, 0, 100, 100);

            Assert.Equal(expected, PieChart.SliceContains(bounds, -90, 90, new Point(x, y)));
        }
    }
}
