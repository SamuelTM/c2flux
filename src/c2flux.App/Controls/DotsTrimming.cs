using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace c2flux
{
    // Text cut with "..." as TextRenderer's EndEllipsis does; Avalonia's
    // CharacterEllipsis draws "…", which at 12 px reads as a single dot.
    public sealed class DotsTrimming : TextTrimming
    {
        public static readonly TextTrimming Instance = new DotsTrimming();

        public override TextCollapsingProperties CreateCollapsingProperties(TextCollapsingCreateInfo createInfo)
        {
            return new TextTrailingCharacterEllipsis("...", createInfo.Width, createInfo.TextRunProperties, createInfo.FlowDirection);
        }

        public override string ToString() => nameof(DotsTrimming);
    }
}
