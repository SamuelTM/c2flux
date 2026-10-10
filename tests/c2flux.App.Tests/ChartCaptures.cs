using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Xunit;

namespace c2flux.AppTests
{
    // Renders the Avalonia charts like c2flux-shots --charts renders the
    // WinForms ones (chart-*.png), for side-by-side comparison (phase 5.2).
    // Files go to $C2FLUX_CHART_OUT, or charts/ next to the test binaries.
    public class ChartCaptures
    {
        private static readonly Color Background = Color.FromRgb(32, 32, 32);

        [AvaloniaFact]
        public void Symbols()
        {
            StatusSymbolKind[] kinds = Enum.GetValues<StatusSymbolKind>();
            int[] sizes = { 14, 48 };
            const int cell = 56;

            string path = Render("chart-symbols", cell * (kinds.Length + 2), cell * sizes.Length, context =>
            {
                for (int row = 0; row < sizes.Length; row++)
                {
                    double offset = (cell - sizes[row]) / 2.0;

                    for (int column = 0; column < kinds.Length + 2; column++)
                    {
                        Rect box = new Rect(column * cell + offset, row * cell + offset, sizes[row], sizes[row]);

                        if (column < kinds.Length)
                        {
                            StatusSymbolRenderer.DrawSymbol(context, box, kinds[column]);
                        }
                        else
                        {
                            StatusSymbolRenderer.DrawTreeExpandGlyph(context, box, column == kinds.Length + 1);
                        }
                    }
                }
            });

            Assert.True(new FileInfo(path).Length > 0);
        }

        // Size of the chart area in the 1280x800 main window, as in c2flux-shots.
        private const int ChartWidth = 890;
        private const int ChartHeight = 630;

        [AvaloniaFact]
        public void Pie()
        {
            PieChart chart = new PieChart();
            chart.SetEntry(LoadFixture());
            Capture("chart-pie", chart, ChartWidth, ChartHeight);
        }

        [AvaloniaFact]
        public void Bar()
        {
            BarChart chart = new BarChart();
            chart.SetEntry(LoadFixture());
            Capture("chart-bar", chart, ChartWidth, ChartHeight);
        }

        [AvaloniaFact]
        public void Sunburst()
        {
            Sunburst chart = new Sunburst();
            chart.SetEntry(LoadFixture());
            Capture("chart-sunburst", chart, ChartWidth, ChartHeight);
        }

        [AvaloniaFact]
        public void Tree()
        {
            EntryTree tree = new EntryTree();
            tree.SetRootEntry(LoadFixture());
            // Size of the tree in the 1280x800 main window.
            Capture("chart-tree", tree, 360, 450);
        }

        internal static FileSystemEntry LoadFixture()
        {
            return ScanResultFileService.Load(Path.Combine(AppContext.BaseDirectory, "chart-tree.json"));
        }

        // The control alone in a borderless window of the given size.
        private static void Capture(string name, Control control, int width, int height)
        {
            Window window = new Window { Width = width, Height = height, Content = control };
            window.Show();

            try
            {
                Dispatcher.UIThread.RunJobs();
                using WriteableBitmap frame = window.CaptureRenderedFrame();
                frame.Save(OutputPath(name));
            }
            finally
            {
                window.Close();
            }
        }

        private static string OutputPath(string name)
        {
            string directory = Environment.GetEnvironmentVariable("C2FLUX_CHART_OUT") ??
                Path.Combine(AppContext.BaseDirectory, "charts");
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, name + ".png");
        }

        private static string Render(string name, int width, int height, Action<DrawingContext> draw)
        {
            using RenderTargetBitmap bitmap = new RenderTargetBitmap(new PixelSize(width, height));

            using (DrawingContext context = bitmap.CreateDrawingContext())
            {
                context.FillRectangle(new SolidColorBrush(Background), new Rect(0, 0, width, height));
                draw(context);
            }

            string path = OutputPath(name);
            bitmap.Save(path);
            return path;
        }
    }
}
