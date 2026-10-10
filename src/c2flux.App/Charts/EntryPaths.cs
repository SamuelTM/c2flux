using System;

namespace c2flux
{
    internal static class EntryPaths
    {
        // Windows accepts / as a separator too and Path.GetDirectoryName
        // returns \, so paths are compared with \ there. Elsewhere \ is a
        // valid file name character and stays.
        public static string ToNativeSeparators(string path)
        {
            return OperatingSystem.IsWindows() ? path.Replace('/', '\\') : path;
        }
    }
}
