using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace c2flux.DiskModel
{
    // Usage: <folder> <groups file>. Prints the seconds taken and writes the
    // groups in a stable form, to compare versions of the analysis.
    internal static class Program
    {
        private static int Main(string[] args)
        {
            FileIdentities.Reader = LinuxFileIdentity.TryRead;
#if !BASELINE
            FileIdentities.ExtentReader = LinuxFileExtents.TryRead;
            FileIdentities.HiddenCloneChecker = LinuxFileExtents.MayHideClones;
            FileIdentities.LocationReader = LinuxFileExtents.TryReadLocation;
#endif

            string root = Path.GetFullPath(args[0]);
            FileSystemEntry[] files = Directory
                .EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(path => new FileSystemEntry { Name = Path.GetFileName(path), FullPath = path, SizeBytes = new FileInfo(path).Length })
                .ToArray();

            Stopwatch watch = Stopwatch.StartNew();
            IReadOnlyList<RedundancyAnalysisGroup> groups = RedundancyAnalysisService.Analyze(files, CancellationToken.None, null, null);
            Console.WriteLine(watch.Elapsed.TotalSeconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture));

            File.WriteAllLines(args[1], groups
                .Select(group => group.SizeBytes + " " + string.Join(" ", group.Locations.Select(path => Path.GetRelativePath(root, path)).OrderBy(path => path, StringComparer.Ordinal)))
                .OrderBy(line => line, StringComparer.Ordinal));
            return 0;
        }
    }
}
