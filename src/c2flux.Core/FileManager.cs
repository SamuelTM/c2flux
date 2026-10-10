using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace c2flux
{
    // "Show in Explorer/Finder/file manager" and "open with the default app".
    public static class FileManager
    {
        // Opens the folder that contains path with path selected. Returns false
        // when no file manager could be started.
        public static bool Reveal(string path)
        {
            foreach ((string fileName, string[] arguments) in RevealCommands(CurrentPlatform(), path))
            {
                if (TryRun(fileName, arguments))
                {
                    return true;
                }
            }

            return false;
        }

        // Opens a file with its default app, or a folder in the file manager.
        public static bool Open(string path)
        {
            try
            {
                using Process process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                return true;
            }
            catch (Exception exception) when (exception is System.ComponentModel.Win32Exception || exception is InvalidOperationException)
            {
                return false;
            }
        }

        // Candidates in order; the first that starts wins. Pure, so the rules
        // of every OS can be tested on any OS.
        internal static IEnumerable<(string FileName, string[] Arguments)> RevealCommands(AppPaths.Platform platform, string path)
        {
            switch (platform)
            {
                case AppPaths.Platform.Windows:
                    yield return ("explorer.exe", new[] { "/select," + path });
                    break;

                case AppPaths.Platform.MacOS:
                    yield return ("open", new[] { "-R", path });
                    break;

                default:
                    // freedesktop FileManager1 selects the item (Nautilus,
                    // Dolphin, Nemo, Thunar, ...); without it, open the folder.
                    yield return ("dbus-send", new[]
                    {
                        "--session",
                        "--print-reply",
                        "--dest=org.freedesktop.FileManager1",
                        "--type=method_call",
                        "/org/freedesktop/FileManager1",
                        "org.freedesktop.FileManager1.ShowItems",
                        "array:string:" + new UriBuilder(Uri.UriSchemeFile, string.Empty) { Path = path }.Uri.AbsoluteUri,
                        "string:",
                    });
                    // Not Path.GetDirectoryName: the rules are built (and tested)
                    // on every OS, and Windows would turn "/" into "\".
                    int slash = path.TrimEnd('/').LastIndexOf('/');
                    yield return ("xdg-open", new[] { slash > 0 ? path.Substring(0, slash) : "/" });
                    break;
            }
        }

        private static bool TryRun(string fileName, string[] arguments)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo(fileName) { UseShellExecute = false };

            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            try
            {
                using Process process = Process.Start(startInfo);

                if (process == null)
                {
                    return false;
                }

                // explorer.exe returns 1 even when it works, and file managers
                // may keep running; only dbus-send's answer is meaningful.
                if (fileName == "dbus-send")
                {
                    return process.WaitForExit(5000) && process.ExitCode == 0;
                }

                return true;
            }
            catch (Exception exception) when (exception is System.ComponentModel.Win32Exception || exception is InvalidOperationException)
            {
                return false;
            }
        }

        private static AppPaths.Platform CurrentPlatform()
        {
            return OperatingSystem.IsWindows() ? AppPaths.Platform.Windows
                : OperatingSystem.IsMacOS() ? AppPaths.Platform.MacOS
                : AppPaths.Platform.Linux;
        }
    }
}
