using System;
using System.IO;
using System.Runtime.InteropServices;

namespace c2flux
{
    // Where c2flux reads its bundled files and stores its own data. Every
    // service builds its paths from here instead of AppContext.BaseDirectory.
    //
    //   Windows  everything next to c2flux.exe, as in v1.4.1 (portable app);
    //            the scan cache stays in %LOCALAPPDATA%\WTF\ScanCache
    //   macOS    ~/Library/Application Support/c2flux, ~/Library/Caches/c2flux
    //   Linux    $XDG_CONFIG_HOME/c2flux, $XDG_DATA_HOME/c2flux,
    //            $XDG_CACHE_HOME/c2flux (with the usual ~/.config,
    //            ~/.local/share and ~/.cache defaults)
    //
    // C2FLUX_HOME, when set, puts all of it under one directory on any system
    // (a portable install on macOS/Linux, or isolated tests).
    //
    // The subfolder names below each root (Settings, ScanHistory, Logs, ...)
    // are the same everywhere, so a Windows install keeps its exact layout.
    public static class AppPaths
    {
        public const string HomeVariable = "C2FLUX_HOME";
        private const string ApplicationFolderName = "c2flux";

        private static readonly Lazy<AppPathSet> Current = new Lazy<AppPathSet>(() =>
            Resolve(
                CurrentPlatform(),
                Environment.GetEnvironmentVariable,
                AppContext.BaseDirectory,
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)));

        // Files shipped with the app (languages, licenses). Read-only: inside a
        // macOS .app bundle or a Linux package it cannot be written.
        public static string ResourcesDirectory => Current.Value.Resources;

        // User settings (Settings\settings.json).
        public static string ConfigDirectory => Current.Value.Config;

        // Scan and storage history, logs, languages added by the user.
        public static string DataDirectory => Current.Value.Data;

        // Data that can be rebuilt (redundancy hash cache).
        public static string CacheDirectory => Current.Value.Cache;

        // Per-folder scan cache of the directory scanner.
        public static string ScanCacheDirectory => Current.Value.ScanCache;

        internal enum Platform
        {
            Windows,
            MacOS,
            Linux,
        }

        internal sealed class AppPathSet
        {
            public string Resources { get; set; }
            public string Config { get; set; }
            public string Data { get; set; }
            public string Cache { get; set; }
            public string ScanCache { get; set; }
        }

        // Pure function of its inputs, so the rules of every system can be
        // tested on any system.
        internal static AppPathSet Resolve(
            Platform platform,
            Func<string, string> environment,
            string baseDirectory,
            string userProfile,
            string localApplicationData)
        {
            string resources = TrimEnd(baseDirectory);
            string home = environment(HomeVariable);

            if (!string.IsNullOrWhiteSpace(home))
            {
                home = TrimEnd(Path.GetFullPath(home));
                return new AppPathSet
                {
                    Resources = resources,
                    Config = home,
                    Data = home,
                    Cache = home,
                    ScanCache = Path.Combine(home, "ScanCache"),
                };
            }

            switch (platform)
            {
                case Platform.Windows:
                    return new AppPathSet
                    {
                        Resources = resources,
                        Config = resources,
                        Data = resources,
                        Cache = resources,
                        ScanCache = Path.Combine(localApplicationData, "WTF", "ScanCache"),
                    };

                case Platform.MacOS:
                {
                    string library = Path.Combine(userProfile, "Library");
                    string support = Path.Combine(library, "Application Support", ApplicationFolderName);
                    string caches = Path.Combine(library, "Caches", ApplicationFolderName);

                    return new AppPathSet
                    {
                        Resources = resources,
                        Config = support,
                        Data = support,
                        Cache = caches,
                        ScanCache = Path.Combine(caches, "ScanCache"),
                    };
                }

                default:
                {
                    string config = Path.Combine(XdgDirectory(environment, "XDG_CONFIG_HOME", userProfile, ".config"), ApplicationFolderName);
                    string data = Path.Combine(XdgDirectory(environment, "XDG_DATA_HOME", userProfile, ".local", "share"), ApplicationFolderName);
                    string cache = Path.Combine(XdgDirectory(environment, "XDG_CACHE_HOME", userProfile, ".cache"), ApplicationFolderName);

                    return new AppPathSet
                    {
                        Resources = resources,
                        Config = config,
                        Data = data,
                        Cache = cache,
                        ScanCache = Path.Combine(cache, "ScanCache"),
                    };
                }
            }
        }

        // The XDG spec says relative values must be ignored.
        private static string XdgDirectory(
            Func<string, string> environment,
            string variable,
            string userProfile,
            params string[] defaultParts)
        {
            string value = environment(variable);

            if (!string.IsNullOrWhiteSpace(value) && Path.IsPathRooted(value))
            {
                return TrimEnd(value);
            }

            string[] parts = new string[defaultParts.Length + 1];
            parts[0] = userProfile;
            Array.Copy(defaultParts, 0, parts, 1, defaultParts.Length);
            return Path.Combine(parts);
        }

        private static Platform CurrentPlatform()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return Platform.Windows;
            }

            return RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? Platform.MacOS
                : Platform.Linux;
        }

        private static string TrimEnd(string path)
        {
            string root = Path.GetPathRoot(path);
            string trimmed = Path.TrimEndingDirectorySeparator(path);
            return string.IsNullOrEmpty(trimmed) ? root : trimmed;
        }
    }
}
