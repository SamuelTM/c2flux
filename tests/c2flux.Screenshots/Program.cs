using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace c2flux.Screenshots
{
    // Opens c2flux windows one by one and saves a PNG of each, plus index.json
    // describing every capture. Each scenario is isolated: a failure is
    // recorded and the next scenario still runs.
    //
    // Usage (from inside the app's publish directory):
    //   c2flux-shots --out <dir> [--scan <path>] [--display 1920x1080]
    //                [--main-size 1280x800] [--only <name>[,<name>...]]
    //
    // Exit codes: 0 all captured, 1 some scenarios failed, 64 invalid arguments.
    internal static class Program
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true,
        };

        [STAThread]
        private static int Main(string[] args)
        {
            Options options;

            try
            {
                options = Options.Parse(args);
            }
            catch (ArgumentException exception)
            {
                Console.Error.WriteLine(exception.Message);
                return 64;
            }

            Directory.CreateDirectory(options.OutputDirectory);
            CaptureLog log = new CaptureLog(options.OutputDirectory);

            log.Display = Display.TrySetResolution(options.DisplayWidth, options.DisplayHeight);
            log.Info("Display: " + log.Display);

            AppSettingsFile.Write(options);

            AppHost app;

            try
            {
                app = AppHost.Load(AppContext.BaseDirectory, log);
            }
            catch (Exception exception)
            {
                log.Info("Could not load the app: " + Unwrap(exception));
                log.Save(JsonOptions);
                return 1;
            }

            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            ApplicationContext context = new ApplicationContext();
            WindowsFormsSynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

            SynchronizationContext.Current.Post(async _ =>
            {
                try
                {
                    app.InitializeLikeProgramMain();
                    Scenarios scenarios = new Scenarios(app, options, log);
                    await scenarios.RunAllAsync();
                }
                catch (Exception exception)
                {
                    log.Info("Fatal: " + Unwrap(exception));
                }
                finally
                {
                    context.ExitThread();
                }
            }, null);

            Application.Run(context);

            log.Save(JsonOptions);
            return log.Captures.Any(capture => capture.Status != "ok") ? 1 : 0;
        }

        internal static Exception Unwrap(Exception exception)
        {
            while (exception is TargetInvocationException && exception.InnerException != null)
            {
                exception = exception.InnerException;
            }

            return exception;
        }
    }

    internal sealed class Options
    {
        public string OutputDirectory { get; private set; }
        public string ScanPath { get; private set; }
        public int DisplayWidth { get; private set; } = 1920;
        public int DisplayHeight { get; private set; } = 1080;
        public int MainWidth { get; private set; } = 1280;
        public int MainHeight { get; private set; } = 800;
        public HashSet<string> Only { get; private set; }

        public bool ShouldRun(string name)
        {
            return Only == null || Only.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

        public static Options Parse(string[] args)
        {
            Options options = new Options();

            for (int index = 0; index < args.Length; index++)
            {
                string argument = args[index];
                string value = index + 1 < args.Length ? args[index + 1] : null;

                switch (argument)
                {
                    case "--out":
                        options.OutputDirectory = Require(value, argument);
                        index++;
                        break;
                    case "--scan":
                        options.ScanPath = Require(value, argument);
                        index++;
                        break;
                    case "--display":
                        (options.DisplayWidth, options.DisplayHeight) = ParseSize(Require(value, argument));
                        index++;
                        break;
                    case "--main-size":
                        (options.MainWidth, options.MainHeight) = ParseSize(Require(value, argument));
                        index++;
                        break;
                    case "--only":
                        options.Only = new HashSet<string>(
                            Require(value, argument).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                            StringComparer.OrdinalIgnoreCase);
                        index++;
                        break;
                    default:
                        throw new ArgumentException("Unknown argument: " + argument);
                }
            }

            if (string.IsNullOrEmpty(options.OutputDirectory))
            {
                throw new ArgumentException("--out is required.");
            }

            options.OutputDirectory = Path.GetFullPath(options.OutputDirectory);
            return options;
        }

        private static string Require(string value, string argument)
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new ArgumentException("Missing value for " + argument);
            }

            return value;
        }

        private static (int, int) ParseSize(string text)
        {
            string[] parts = text.ToLowerInvariant().Split('x');

            if (parts.Length != 2 || !int.TryParse(parts[0], out int width) || !int.TryParse(parts[1], out int height))
            {
                throw new ArgumentException("Size must look like 1280x800, got " + text);
            }

            return (width, height);
        }
    }

    // c2flux reads Settings\settings.json next to the executable. Writing it
    // before the app loads gives every capture the same, predictable state:
    // English, no update check, no elevation prompt, fixed main window bounds.
    internal static class AppSettingsFile
    {
        public static void Write(Options options)
        {
            string directory = Path.Combine(AppContext.BaseDirectory, "Settings");
            Directory.CreateDirectory(directory);

            Dictionary<string, object> settings = new Dictionary<string, object>
            {
                ["LanguageCode"] = "en",
                ["AutoCheckForUpdates"] = false,
                ["ShowElevationPromptOnStartup"] = false,
                ["StartElevatedOnStartup"] = false,
                ["ShellContextMenuEnabled"] = false,
                ["ShellSearchContextMenuEnabled"] = false,
                // On, so the second scan of the run fills the scan history.
                ["SaveScanHistory"] = true,
                // Off by default in the app; on, the storage history details
                // window lists what changed between the two scans.
                ["StorageHistoryDetailsEnabled"] = true,
                ["SelectedViewMode"] = 0,
                ["HasMainWindowBounds"] = true,
                ["MainWindowLeft"] = 0,
                ["MainWindowTop"] = 0,
                ["MainWindowWidth"] = options.MainWidth,
                ["MainWindowHeight"] = options.MainHeight,
                ["MainWindowMaximized"] = false,
            };

            File.WriteAllText(Path.Combine(directory, "settings.json"), JsonSerializer.Serialize(settings));
        }
    }

    // Loads c2flux.dll into the default load context (so WinForms types are
    // shared with this tool) and resolves its dependencies from c2flux.deps.json.
    internal sealed class AppHost
    {
        private readonly Assembly _assembly;

        private AppHost(Assembly assembly)
        {
            _assembly = assembly;
        }

        public string Version
        {
            get
            {
                return _assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            }
        }

        public static AppHost Load(string directory, CaptureLog log)
        {
            string mainAssemblyPath = Path.Combine(directory, "c2flux.dll");

            if (!File.Exists(mainAssemblyPath))
            {
                throw new FileNotFoundException(
                    "c2flux.dll not found. Copy c2flux-shots into the app's publish directory.",
                    mainAssemblyPath);
            }

            AssemblyDependencyResolver resolver = new AssemblyDependencyResolver(mainAssemblyPath);

            AssemblyLoadContext.Default.Resolving += (context, name) =>
            {
                string path = resolver.ResolveAssemblyToPath(name);
                return path == null ? null : context.LoadFromAssemblyPath(path);
            };

            AssemblyLoadContext.Default.ResolvingUnmanagedDll += (assembly, name) =>
            {
                string path = resolver.ResolveUnmanagedDllToPath(name);
                return path == null ? IntPtr.Zero : NativeLibraryLoad(path);
            };

            AppHost host = new AppHost(AssemblyLoadContext.Default.LoadFromAssemblyPath(mainAssemblyPath));
            log.AppVersion = host.Version;
            return host;
        }

        private static IntPtr NativeLibraryLoad(string path)
        {
            return System.Runtime.InteropServices.NativeLibrary.Load(path);
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

                type = AssemblyLoadContext.Default.LoadFromAssemblyName(reference).GetType(fullName, false);

                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        public Type GetType(string name)
        {
            Type type = FindAppType("c2flux." + name);

            if (type == null)
            {
                throw new InvalidOperationException("Type c2flux." + name + " not found in this app version.");
            }

            return type;
        }

        public object CallStatic(string typeName, string methodName, params object[] arguments)
        {
            MethodInfo method = GetType(typeName)
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .FirstOrDefault(candidate => candidate.Name == methodName && candidate.GetParameters().Length == arguments.Length);

            if (method == null)
            {
                throw new InvalidOperationException(typeName + "." + methodName + " not found.");
            }

            return method.Invoke(null, arguments);
        }

        public object LoadSettings()
        {
            return CallStatic("AppSettings", "Load");
        }

        // Same order as c2flux.Program.Main, minus dialogs, shell integration
        // and elevation.
        public void InitializeLikeProgramMain()
        {
            object settings = LoadSettings();
            object layout = Get(settings, "Layout");

            CallStatic("AntdThemeService", "Apply", layout);
            CallStatic("LocalizationService", "Initialize", (string)Get(settings, "LanguageCode"));
            CallStatic("AntdThemeService", "ConfigureLocalization");
        }

        public Form CreateForm(string typeName, params object[] arguments)
        {
            Type type = GetType(typeName);

            foreach (ConstructorInfo constructor in type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                ParameterInfo[] parameters = constructor.GetParameters();

                if (parameters.Length < arguments.Length ||
                    parameters.Skip(arguments.Length).Any(parameter => !parameter.HasDefaultValue))
                {
                    continue;
                }

                bool matches = true;

                for (int index = 0; index < arguments.Length; index++)
                {
                    if (arguments[index] != null && !parameters[index].ParameterType.IsInstanceOfType(arguments[index]))
                    {
                        matches = false;
                        break;
                    }
                }

                if (!matches)
                {
                    continue;
                }

                object[] values = arguments
                    .Concat(parameters.Skip(arguments.Length).Select(parameter => parameter.DefaultValue))
                    .ToArray();

                return (Form)constructor.Invoke(values);
            }

            throw new InvalidOperationException("No matching constructor for " + typeName + ".");
        }

        public static object Get(object instance, string memberName)
        {
            Type type = instance.GetType();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            PropertyInfo property = type.GetProperty(memberName, flags);

            if (property != null)
            {
                return property.GetValue(instance);
            }

            FieldInfo field = type.GetField(memberName, flags);

            if (field != null)
            {
                return field.GetValue(instance);
            }

            throw new InvalidOperationException(type.Name + "." + memberName + " not found.");
        }

        public static void Invoke(object instance, string methodName, params object[] arguments)
        {
            MethodInfo method = instance.GetType().GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            if (method == null)
            {
                throw new InvalidOperationException(instance.GetType().Name + "." + methodName + " not found.");
            }

            method.Invoke(instance, arguments);
        }
    }

    internal sealed class CaptureLog
    {
        private readonly string _directory;
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        public CaptureLog(string directory)
        {
            _directory = directory;
        }

        public string AppVersion { get; set; }
        public string Display { get; set; }
        public List<CaptureRecord> Captures { get; } = new List<CaptureRecord>();
        public List<string> Messages { get; } = new List<string>();

        public void Info(string message)
        {
            string line = string.Format("[{0,7:0.0}s] {1}", _clock.Elapsed.TotalSeconds, message);
            Messages.Add(line);
            Console.WriteLine(line);
        }

        public void Save(JsonSerializerOptions options)
        {
            var index = new
            {
                AppVersion,
                Display,
                Os = Environment.OSVersion.VersionString,
                Dpi = Screen.PrimaryScreen == null ? (int?)null : DpiOfPrimaryScreen(),
                Captures,
                Messages,
            };

            File.WriteAllText(Path.Combine(_directory, "index.json"), JsonSerializer.Serialize(index, options));
        }

        private static int DpiOfPrimaryScreen()
        {
            using (System.Drawing.Graphics graphics = System.Drawing.Graphics.FromHwnd(IntPtr.Zero))
            {
                return (int)graphics.DpiX;
            }
        }
    }

    internal sealed class CaptureRecord
    {
        public string Name { get; set; }
        public string File { get; set; }
        public string Status { get; set; }
        public string Error { get; set; }
        public string Method { get; set; }
        public string FormType { get; set; }
        public string Title { get; set; }
        public int? Width { get; set; }
        public int? Height { get; set; }
    }
}
