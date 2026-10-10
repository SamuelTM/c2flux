using System;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace c2flux.Benchmarks
{
    // Runs one c2flux scanner once and prints one JSON object with timing,
    // memory and a fingerprint of the resulting tree. Meant to be started in a
    // fresh process per measurement by run_benchmark.py.
    //
    // Exit codes: 0 measured, 1 scanner failed, 2 scanner not supported for the
    // path, 3 scanner not present in this app version, 64 invalid arguments.
    internal static class Program
    {
        private const string Usage =
            "Usage: c2flux-bench --app <app build dir or c2flux.dll> --scanner <key> --path <path> [--dump <file>]\n" +
            "       c2flux-bench --list";

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        private static async Task<int> Main(string[] args)
        {
            Options options;

            try
            {
                options = Options.Parse(args);
            }
            catch (ArgumentException exception)
            {
                Console.Error.WriteLine(exception.Message);
                Console.Error.WriteLine(Usage);
                return 64;
            }

            if (options.List)
            {
                foreach (ScannerDefinition definition in ScannerDefinition.All)
                {
                    Console.WriteLine(definition.Key);
                }

                return 0;
            }

            BenchmarkResult result = new BenchmarkResult
            {
                Scanner = options.ScannerKey,
                Path = options.Path,
                Os = Environment.OSVersion.VersionString,
                ProcessorCount = Environment.ProcessorCount,
            };

            int exitCode;

            try
            {
                exitCode = await RunAsync(options, result);
            }
            catch (Exception exception)
            {
                Exception cause = Unwrap(exception);
                result.Status = "error";
                result.Error = cause.GetType().FullName + ": " + cause.Message;
                result.ErrorDetail = cause.ToString();
                exitCode = 1;
            }

            Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            return exitCode;
        }

        private static async Task<int> RunAsync(Options options, BenchmarkResult result)
        {
            ScannerDefinition definition = ScannerDefinition.Find(options.ScannerKey);
            AppLoader app = AppLoader.Load(options.AppPath, options.MainAssemblyName);
            result.AppVersion = app.InformationalVersion;

            Type scannerType = app.FindScannerType(definition);

            if (scannerType == null)
            {
                // The scanner was added after this app version.
                result.Status = "missing";
                result.Error = "Scanner not present in this app version (" + string.Join(", ", definition.TypeNames) + ")";
                return 3;
            }

            result.ScannerType = scannerType.FullName;

            string unsupportedReason = app.GetUnsupportedReason(definition, options.Path);

            if (unsupportedReason != null)
            {
                result.Status = "unsupported";
                result.Error = unsupportedReason;
                return 2;
            }

            ScanInvocation invocation = app.PrepareScan(scannerType, options.Path);

            // Start from a clean heap so the numbers reflect the scan only.
            long memoryBefore = GC.GetTotalMemory(true);
            long allocatedBefore = GC.GetTotalAllocatedBytes(true);
            int gen0Before = GC.CollectionCount(0);
            int gen1Before = GC.CollectionCount(1);
            int gen2Before = GC.CollectionCount(2);

            Stopwatch stopwatch = Stopwatch.StartNew();
            object rootEntry = await invocation.RunAsync();
            stopwatch.Stop();

            long allocatedAfter = GC.GetTotalAllocatedBytes(true);
            int gen0After = GC.CollectionCount(0);
            int gen1After = GC.CollectionCount(1);
            int gen2After = GC.CollectionCount(2);
            long peakWorkingSet = Process.GetCurrentProcess().PeakWorkingSet64;

            // Full collection while the tree is still referenced: what remains is
            // what the app has to keep in memory to show the result.
            long memoryAfter = GC.GetTotalMemory(true);

            result.Status = "ok";
            result.ElapsedMs = stopwatch.Elapsed.TotalMilliseconds;
            result.AllocatedBytes = allocatedAfter - allocatedBefore;
            result.RetainedBytes = memoryAfter - memoryBefore;
            result.PeakWorkingSetBytes = peakWorkingSet;
            result.GcCollections = new[] { gen0After - gen0Before, gen1After - gen1Before, gen2After - gen2Before };
            result.ProgressReports = invocation.Progress.Count;
            result.Tree = TreeFingerprint.Compute(rootEntry);

            if (!string.IsNullOrEmpty(options.DumpPath))
            {
                System.IO.File.WriteAllLines(options.DumpPath, result.Tree.Lines);
            }

            GC.KeepAlive(rootEntry);
            return 0;
        }

        private static Exception Unwrap(Exception exception)
        {
            while (true)
            {
                if (exception is TargetInvocationException && exception.InnerException != null)
                {
                    exception = exception.InnerException;
                }
                else if (exception is AggregateException aggregate && aggregate.InnerExceptions.Count == 1)
                {
                    exception = aggregate.InnerExceptions[0];
                }
                else
                {
                    return exception;
                }
            }
        }

        private sealed class Options
        {
            public string AppPath { get; private set; }
            public string MainAssemblyName { get; private set; } = "c2flux.dll";
            public string ScannerKey { get; private set; }
            public string Path { get; private set; }
            public string DumpPath { get; private set; }
            public bool List { get; private set; }

            public static Options Parse(string[] args)
            {
                Options options = new Options();

                for (int index = 0; index < args.Length; index++)
                {
                    string argument = args[index];

                    switch (argument)
                    {
                        case "--app":
                            options.AppPath = NextValue(args, ref index, argument);
                            break;
                        case "--main-assembly":
                            options.MainAssemblyName = NextValue(args, ref index, argument);
                            break;
                        case "--scanner":
                            options.ScannerKey = NextValue(args, ref index, argument);
                            break;
                        case "--dump":
                            options.DumpPath = NextValue(args, ref index, argument);
                            break;
                        case "--path":
                            options.Path = NextValue(args, ref index, argument);
                            break;
                        case "--list":
                            options.List = true;
                            break;
                        default:
                            throw new ArgumentException("Unknown argument: " + argument);
                    }
                }

                if (!options.List &&
                    (string.IsNullOrEmpty(options.AppPath) ||
                     string.IsNullOrEmpty(options.ScannerKey) ||
                     string.IsNullOrEmpty(options.Path)))
                {
                    throw new ArgumentException("--app, --scanner and --path are required.");
                }

                return options;
            }

            private static string NextValue(string[] args, ref int index, string argument)
            {
                if (index + 1 >= args.Length)
                {
                    throw new ArgumentException("Missing value for " + argument);
                }

                index++;
                return args[index];
            }
        }
    }

    internal sealed class BenchmarkResult
    {
        public string Status { get; set; }
        public string Error { get; set; }
        public string ErrorDetail { get; set; }
        public string Scanner { get; set; }
        public string ScannerType { get; set; }
        public string Path { get; set; }
        public string AppVersion { get; set; }
        public string Os { get; set; }
        public int ProcessorCount { get; set; }
        public double? ElapsedMs { get; set; }
        public long? AllocatedBytes { get; set; }
        public long? RetainedBytes { get; set; }
        public long? PeakWorkingSetBytes { get; set; }
        public int[] GcCollections { get; set; }
        public int? ProgressReports { get; set; }
        public TreeFingerprint Tree { get; set; }
    }
}
