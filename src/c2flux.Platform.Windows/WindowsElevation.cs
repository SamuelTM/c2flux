using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;

namespace c2flux
{
    // Administrator rights: the MFT scanner needs them. Port of the WinForms
    // Program helpers.
    [SupportedOSPlatform("windows")]
    public static class WindowsElevation
    {
        public static bool IsElevated()
        {
            try
            {
                using System.Security.Principal.WindowsIdentity identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                return new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch (Exception exception) when (exception is System.Security.SecurityException || exception is UnauthorizedAccessException)
            {
                return false;
            }
        }

        // Starts this executable again through UAC ("runas") with the same
        // arguments. False when the user declined or it failed.
        public static bool TryRestartElevated(IEnumerable<string> args)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = Environment.ProcessPath,
                    UseShellExecute = true,
                    Verb = "runas",
                    WorkingDirectory = AppContext.BaseDirectory,
                    Arguments = string.Join(" ", (args ?? Array.Empty<string>()).Select(Quote)),
                });
                return true;
            }
            catch (Exception exception) when (exception is System.ComponentModel.Win32Exception || exception is InvalidOperationException)
            {
                return false;
            }
        }

        // CommandLineToArgvW quoting: backslashes before a quote are doubled.
        internal static string Quote(string argument)
        {
            if (argument == null)
            {
                return "\"\"";
            }

            StringBuilder quoted = new StringBuilder("\"");
            int backslashes = 0;

            foreach (char character in argument)
            {
                if (character == '\\')
                {
                    backslashes++;
                    continue;
                }

                quoted.Append('\\', character == '"' ? backslashes * 2 + 1 : backslashes);
                quoted.Append(character);
                backslashes = 0;
            }

            return quoted.Append('\\', backslashes * 2).Append('"').ToString();
        }
    }
}
