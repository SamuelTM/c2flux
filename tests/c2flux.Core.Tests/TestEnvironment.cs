using System;
using System.IO;
using System.Runtime.CompilerServices;

// Core services keep global static state (database path, loaded language,
// settings file), so tests in different classes must not run at the same time.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace c2flux.Core.Tests
{
    // Core services keep their paths in static fields that are initialized on
    // first use. This runs before any test, so everything the tests write
    // (settings, histories, languages) goes to a throwaway directory instead
    // of the developer's real c2flux data.
    internal static class TestEnvironment
    {
        public static string Home { get; private set; }

        [ModuleInitializer]
        internal static void Initialize()
        {
            Home = Path.Combine(Path.GetTempPath(), "c2flux-core-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Home);
            Environment.SetEnvironmentVariable(AppPaths.HomeVariable, Home);
        }

        public static string CreateDirectory(string name)
        {
            string path = Path.Combine(Home, name + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
