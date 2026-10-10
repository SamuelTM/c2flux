using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;

namespace c2flux.Benchmarks
{
    // Scanners the tool knows how to run. Each key lists the type names it has
    // had across versions, newest first, so the same tool build can measure the
    // baseline and the refactored code. Add the new name here when a scanner
    // class is renamed or moved.
    internal sealed class ScannerDefinition
    {
        public static readonly ScannerDefinition[] All =
        {
            new ScannerDefinition("c2flux", true, "c2flux.C2FluxScanner"),
            new ScannerDefinition("ntfsmft", true, "c2flux.NtfsMftScanner"),
            new ScannerDefinition("ntquery", false, "c2flux.NtQueryDirectoryScanner"),
            new ScannerDefinition("win32find", false, "c2flux.DirectoryScanner"),
            // Portable scanner in c2flux.Core since phase 2; absent in older versions.
            new ScannerDefinition("managed", false, "c2flux.ManagedScanner"),
        };

        private ScannerDefinition(string key, bool requiresMft, params string[] typeNames)
        {
            Key = key;
            RequiresMft = requiresMft;
            TypeNames = typeNames;
        }

        public string Key { get; }
        public bool RequiresMft { get; }
        public string[] TypeNames { get; }

        public static ScannerDefinition Find(string key)
        {
            ScannerDefinition definition = All.FirstOrDefault(
                candidate => string.Equals(candidate.Key, key, StringComparison.OrdinalIgnoreCase));

            if (definition == null)
            {
                throw new ArgumentException(
                    string.Format(
                        "Unknown scanner '{0}'. Known scanners: {1}.",
                        key,
                        string.Join(", ", All.Select(candidate => candidate.Key))));
            }

            return definition;
        }
    }

    // Isolates the app and its dependencies (NtfsReader, SQLite, AntdUI) using
    // the app's own deps.json, the same way the app resolves them at startup.
    internal sealed class AppLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;

        public AppLoadContext(string mainAssemblyPath)
            : base("c2flux-app")
        {
            _resolver = new AssemblyDependencyResolver(mainAssemblyPath);
        }

        protected override Assembly Load(AssemblyName assemblyName)
        {
            string path = _resolver.ResolveAssemblyToPath(assemblyName);
            return path == null ? null : LoadFromAssemblyPath(path);
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            string path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return path == null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
        }
    }

    internal sealed class AppLoader
    {
        private const string MftSupportTypeName = "c2flux.NtfsMftScanner";

        private readonly Assembly _assembly;

        private AppLoader(Assembly assembly)
        {
            _assembly = assembly;
        }

        public string InformationalVersion
        {
            get
            {
                AssemblyInformationalVersionAttribute attribute =
                    _assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();

                return attribute?.InformationalVersion ?? _assembly.GetName().Version?.ToString();
            }
        }

        public static AppLoader Load(string appPath, string mainAssemblyName)
        {
            string mainAssemblyPath = Directory.Exists(appPath)
                ? Path.Combine(appPath, mainAssemblyName)
                : appPath;

            mainAssemblyPath = Path.GetFullPath(mainAssemblyPath);

            if (!File.Exists(mainAssemblyPath))
            {
                throw new FileNotFoundException("App assembly not found.", mainAssemblyPath);
            }

            AppLoadContext loadContext = new AppLoadContext(mainAssemblyPath);
            return new AppLoader(loadContext.LoadFromAssemblyPath(mainAssemblyPath));
        }

        // The app's own assembly first, then the c2flux.* assemblies it
        // references (c2flux.Core since phase 1). Older versions, such as
        // baseline-winforms, have everything in c2flux.dll.
        private Type FindAppType(string fullName)
        {
            Type type = _assembly.GetType(fullName, false);

            if (type != null)
            {
                return type;
            }

            foreach (AssemblyName reference in _assembly.GetReferencedAssemblies())
            {
                if (!reference.Name.StartsWith("c2flux", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                type = AssemblyLoadContext.GetLoadContext(_assembly).LoadFromAssemblyName(reference).GetType(fullName, false);

                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        public Type FindScannerType(ScannerDefinition definition)
        {
            foreach (string typeName in definition.TypeNames)
            {
                Type type = FindAppType(typeName);

                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        // Mirrors the check the app makes before choosing an MFT
        // scanner. Returns null when the scanner can run on the path.
        public string GetUnsupportedReason(ScannerDefinition definition, string path)
        {
            if (!definition.RequiresMft)
            {
                return null;
            }

            // MFT scanners always read the whole volume.
            string root = Path.GetPathRoot(Path.GetFullPath(path));

            if (!string.Equals(
                    Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)),
                    Path.TrimEndingDirectorySeparator(root ?? string.Empty),
                    StringComparison.OrdinalIgnoreCase))
            {
                return "MFT scanners only accept a drive root such as C:\\";
            }

            Type supportType = FindAppType(MftSupportTypeName);
            MethodInfo isSupported = supportType?.GetMethod(
                "IsSupported",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(string) },
                null);

            if (isSupported == null)
            {
                return "MFT support check not found in this app version";
            }

            return (bool)isSupported.Invoke(null, new object[] { path })
                ? null
                : "MFT scanning is not supported for this path (needs an NTFS drive root and administrator rights)";
        }

        public ScanInvocation PrepareScan(Type scannerType, string path)
        {
            ConstructorInfo constructor = scannerType
                .GetConstructors()
                .FirstOrDefault(candidate =>
                {
                    ParameterInfo[] parameters = candidate.GetParameters();
                    return parameters.Length == 1 && parameters[0].ParameterType.Name == "AppSettings";
                });

            if (constructor == null)
            {
                throw new InvalidOperationException(
                    scannerType.FullName + " has no constructor taking AppSettings.");
            }

            // Default settings, as on a fresh install. Never loaded from disk so
            // that both versions run with identical options.
            Type settingsType = constructor.GetParameters()[0].ParameterType;
            object settings = Activator.CreateInstance(settingsType);
            object scanner = constructor.Invoke(new[] { settings });

            // Extra parameters are allowed as long as they are optional; they get
            // their default values, as when the app calls the method.
            MethodInfo scanMethod = scannerType
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(candidate =>
                    candidate.Name == "ScanAsync" &&
                    candidate.GetParameters().Length >= 4 &&
                    candidate.GetParameters()[0].ParameterType == typeof(string) &&
                    candidate.GetParameters().Skip(4).All(parameter => parameter.HasDefaultValue));

            if (scanMethod == null)
            {
                throw new InvalidOperationException(
                    scannerType.FullName + " has no ScanAsync(string, IProgress<>, CancellationToken, PauseToken).");
            }

            ParameterInfo[] scanParameters = scanMethod.GetParameters();
            Type progressValueType = scanParameters[1].ParameterType.GetGenericArguments()[0];

            ICountingProgress progress = (ICountingProgress)Activator.CreateInstance(
                typeof(CountingProgress<>).MakeGenericType(progressValueType));

            // default(PauseToken) never pauses.
            object pauseToken = Activator.CreateInstance(scanParameters[3].ParameterType);

            object[] arguments = new object[scanParameters.Length];
            arguments[0] = path;
            arguments[1] = progress;
            arguments[2] = CancellationToken.None;
            arguments[3] = pauseToken;

            for (int index = 4; index < scanParameters.Length; index++)
            {
                arguments[index] = scanParameters[index].DefaultValue;
            }

            return new ScanInvocation(scanner, scanMethod, arguments, progress);
        }
    }

    internal sealed class ScanInvocation
    {
        private readonly object _scanner;
        private readonly MethodInfo _scanMethod;
        private readonly object[] _arguments;

        public ScanInvocation(
            object scanner,
            MethodInfo scanMethod,
            object[] arguments,
            ICountingProgress progress)
        {
            _scanner = scanner;
            _scanMethod = scanMethod;
            _arguments = arguments;
            Progress = progress;
        }

        public ICountingProgress Progress { get; }

        public async Task<object> RunAsync()
        {
            Task task = (Task)_scanMethod.Invoke(_scanner, _arguments);

            await task.ConfigureAwait(false);

            return task.GetType().GetProperty("Result").GetValue(task);
        }
    }

    internal interface ICountingProgress
    {
        int Count { get; }
    }

    // Stands in for the UI's Progress<ScanProgress>: the scanner still builds
    // and reports every progress object, but nothing is marshalled to a UI thread.
    internal sealed class CountingProgress<T> : IProgress<T>, ICountingProgress
    {
        private int _count;

        public int Count
        {
            get { return Volatile.Read(ref _count); }
        }

        public void Report(T value)
        {
            Interlocked.Increment(ref _count);
        }
    }
}
