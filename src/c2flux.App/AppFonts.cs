using Avalonia;
using Avalonia.Media;

namespace c2flux
{
    public static class AppFonts
    {
        // Inter on every OS, also for FontFamily.Default (drawn text);
        // WithInterFont alone leaves the default at the system font.
        // Scripts Inter lacks (CJK, Thai, Devanagari) fall back to system fonts.
        public static AppBuilder UseAppFonts(this AppBuilder builder)
        {
            return builder
                .WithInterFont()
                .With(new FontManagerOptions { DefaultFamilyName = "fonts:Inter#Inter" });
        }
    }
}
