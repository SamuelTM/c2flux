using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace c2flux
{
    // A StatusSymbolRenderer symbol as a control (status bar alert counters).
    public sealed class StatusSymbolIcon : Control
    {
        public static readonly StyledProperty<StatusSymbolKind> KindProperty =
            AvaloniaProperty.Register<StatusSymbolIcon, StatusSymbolKind>(nameof(Kind));

        static StatusSymbolIcon()
        {
            AffectsRender<StatusSymbolIcon>(KindProperty);
        }

        public StatusSymbolKind Kind
        {
            get => GetValue(KindProperty);
            set => SetValue(KindProperty, value);
        }

        public override void Render(DrawingContext context)
        {
            StatusSymbolRenderer.DrawSymbol(context, new Rect(Bounds.Size), Kind);
        }
    }
}
