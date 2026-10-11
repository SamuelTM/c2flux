using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace c2flux
{
    // FileManager.TrashMover for Linux: GLib's "gio trash", which follows the
    // freedesktop trash spec (the home trash, or .Trash-<uid> on another
    // volume) like the desktop's file managers.
    // shortcut: needs gio (GLib, on GNOME, KDE and most desktops); a system
    // without it gets an error, not the spec done by hand.
    public static class LinuxTrash
    {
        public static void Move(string path)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo("gio") { UseShellExecute = false, RedirectStandardError = true };
            startInfo.ArgumentList.Add("trash");
            startInfo.ArgumentList.Add("--");
            startInfo.ArgumentList.Add(Path.GetFullPath(path));

            try
            {
                using Process process = Process.Start(startInfo);
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    throw new IOException(string.IsNullOrWhiteSpace(error) ? "gio could not move the item to the trash." : error.Trim());
                }
            }
            catch (Win32Exception)
            {
                throw new IOException("Moving to the trash needs gio (GLib), which is not installed.");
            }
        }
    }
}
