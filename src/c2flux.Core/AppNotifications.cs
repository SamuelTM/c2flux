using System;

namespace c2flux
{
    // Lets Core services show a warning to the user without depending on a UI
    // toolkit. The UI registers its message box at startup (see the WinForms
    // Program.Main); until then, and in tools or tests, nothing is shown and
    // the callers' alert log entries are the only trace.
    public static class AppNotifications
    {
        // settings may be null when the caller has no AppSettings instance;
        // the UI then picks the current theme itself.
        public static Action<AppSettings, string, string, string> WarningHandler { get; set; }

        public static void ShowWarning(
            AppSettings settings,
            string messageText,
            string title,
            string okButtonText)
        {
            WarningHandler?.Invoke(settings, messageText, title, okButtonText);
        }
    }
}
