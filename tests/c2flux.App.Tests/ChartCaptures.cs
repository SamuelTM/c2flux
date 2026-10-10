using System;
using System.IO;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
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

        private static string Render(string name, int width, int height, Action<DrawingContext> draw)
        {
            using RenderTargetBitmap bitmap = new RenderTargetBitmap(new PixelSize(width, height));

            using (DrawingContext context = bitmap.CreateDrawingContext())
            {
                context.FillRectangle(new SolidColorBrush(Background), new Rect(0, 0, width, height));
                draw(context);
            }

            string directory = Environment.GetEnvironmentVariable("C2FLUX_CHART_OUT") ??
                Path.Combine(AppContext.BaseDirectory, "charts");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, name + ".png");
            bitmap.Save(path);
            return path;
        }
    }
}
