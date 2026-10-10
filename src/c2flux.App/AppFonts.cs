using Avalonia;
using Avalonia.Media;

namespace c2flux
{
    public static class AppFonts
    {
        // Selawik (SIL OFL, Microsoft): metric-compatible with Segoe UI, the
        // font of the WinForms app, so text has the same widths on every OS.
        // Scripts it lacks (CJK, Thai, Devanagari, ...) fall back to system
        // fonts.
        public const string Family = "avares://c2flux.App/Assets/Fonts#Selawik";

        public static AppBuilder UseAppFonts(this AppBuilder builder)
        {
            return builder.With(new FontManagerOptions { DefaultFamilyName = Family });
        }
    }
}
