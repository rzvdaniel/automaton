using Python.Runtime;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace Automaton.Core.Scripting.Engines.Python
{
    public class PythonNetScriptEngine : IScriptEngine, IOutputCallbackScriptEngine, IDisposable
    {
        private readonly object initLock = new object();
        private static readonly object gilExecutionLock = new object();
        private static readonly object packageInstallLock = new object();
        private readonly StringBuilder outputBuffer = new StringBuilder();
        private readonly ScriptEngineConfiguration configuration;
        private IntPtr _allowThreadsState = IntPtr.Zero;

        // Extracted environment detection/configuration
        private readonly PythonEnvironmentInfo detectedEnv;
        private readonly PythonEnvironmentDetector detector;
        private readonly PythonEnvironmentConfigurator configurator;

        public Action<string>? OutputCallback { get; set; }

        // Cache of modules already verified/installed — static so it survives across executions
        private static readonly HashSet<string> _verifiedModules = new(StringComparer.OrdinalIgnoreCase);

        // Regex that extracts the root package name from import/from statements
        private static readonly Regex ImportRegex = new(
            @"^(?:import|from)\s+([a-zA-Z_][a-zA-Z0-9_]*)",
            RegexOptions.Multiline | RegexOptions.Compiled);

        // Maps Python import names to their pip package names where they differ
        private static readonly IReadOnlyDictionary<string, string> ImportToPipName =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["PIL"]               = "Pillow",
            ["cv2"]               = "opencv-python",
            ["sklearn"]           = "scikit-learn",
            ["bs4"]               = "beautifulsoup4",
            ["yaml"]              = "pyyaml",
            ["dateutil"]          = "python-dateutil",
            ["dotenv"]            = "python-dotenv",
            ["pydub"]             = "pydub",
            ["librosa"]           = "librosa",
            ["soundfile"]         = "soundfile",
            ["speech_recognition"]= "SpeechRecognition",
            ["ffmpeg"]            = "ffmpeg-python",
            ["moviepy"]           = "moviepy",
            ["jwt"]               = "PyJWT",
            ["serial"]            = "pyserial",
            ["openpyxl"]          = "openpyxl",
            ["xlrd"]              = "xlrd",
            ["pymongo"]           = "pymongo",
            ["sqlalchemy"]        = "SQLAlchemy",
            ["flask"]             = "flask",
            ["fastapi"]           = "fastapi",
            ["uvicorn"]           = "uvicorn",
            ["django"]            = "django",
            ["pydantic"]          = "pydantic",
            ["paramiko"]          = "paramiko",
            ["redis"]             = "redis",
            ["celery"]            = "celery",
            ["anthropic"]         = "anthropic",
            ["openai"]            = "openai",
            ["stripe"]            = "stripe",
            ["twilio"]            = "twilio",
            ["sendgrid"]          = "sendgrid",
            ["selenium"]          = "selenium",
            ["playwright"]        = "playwright",
            ["scrapy"]            = "scrapy",
        };

        // Python standard library modules — never attempt to pip-install these
        private static readonly HashSet<string> StandardLibraryModules = new(StringComparer.OrdinalIgnoreCase)
        {
            "sys", "os", "io", "re", "json", "math", "random", "datetime", "time", "calendar",
            "collections", "itertools", "functools", "pathlib", "shutil", "tempfile", "glob",
            "fnmatch", "struct", "hashlib", "hmac", "secrets", "base64", "binascii", "codecs",
            "string", "textwrap", "unicodedata", "enum", "typing", "abc", "dataclasses",
            "copy", "pprint", "reprlib", "operator", "contextlib", "gc", "inspect", "dis",
            "traceback", "warnings", "logging", "unittest", "doctest", "pdb", "timeit",
            "socket", "ssl", "email", "html", "http", "urllib", "xml", "csv", "sqlite3",
            "zipfile", "tarfile", "gzip", "bz2", "lzma", "zlib", "pickle", "shelve",
            "multiprocessing", "threading", "queue", "asyncio", "concurrent", "subprocess",
            "signal", "ctypes", "platform", "sysconfig", "importlib", "pkgutil", "ast",
            "tokenize", "keyword", "builtins", "site", "argparse", "configparser",
            "numbers", "decimal", "fractions", "statistics", "array", "bisect", "heapq",
            "weakref", "types", "__future__", "atexit", "cmath", "getopt", "optparse",
        };

        public PythonNetScriptEngine() : this(new ScriptEngineConfiguration())
        {
        }

        public PythonNetScriptEngine(ScriptEngineConfiguration configuration)
        {
            this.configuration = configuration;
            this.detector = new PythonEnvironmentDetector();
            this.configurator = new PythonEnvironmentConfigurator();
            this.detectedEnv = detector.Detect();
            // Do not call InitializePython() here - defer until Execute() is called
        }

        private void InitializePython()
        {
            lock (initLock)
            {
                try
                {
                    // Only initialize Python.NET if not already initialized globally
                    if (!PythonEngine.IsInitialized)
                    {
                        // Set Python DLL before initialization if not already set
                        if (string.IsNullOrWhiteSpace(Runtime.PythonDLL))
                        {
                            configurator.Configure(detectedEnv, configuration);
                        }

                        // Initialize Python and release the GIL so ASP.NET threads are not blocked
                        PythonEngine.Initialize();
                        _allowThreadsState = PythonEngine.BeginAllowThreads();
                    }

                    // Always apply configured paths for this engine instance, even if Python was already initialized
                    ApplyConfiguredPathsToSyspath();
                }
                catch (Exception ex)
                {
                    throw;
                }
            }
        }

        /// <summary>
        /// Apply configured paths directly to Python's sys.path
        /// Priority: user-configured paths (searched first) > auto-detected paths
        /// 
        /// BEHAVIOR: Both user paths AND auto-detected paths are added:
        /// 1. User-configured paths are added first (highest priority in search order)
        /// 2. Auto-detected paths are added second (fallback if user config incomplete)
        /// 3. Duplicates are removed automatically via .Distinct()
        /// 4. Non-existent paths are filtered out
        ///
        /// This approach provides robustness: if user configuration is incomplete or contains
        /// typos, auto-detection fills the gaps. If auto-detection finds something user needs,
        /// it's available as fallback. User retains full control via priority ordering.
        /// 
        /// No environment variables are set; paths are injected directly into the running interpreter.
        /// </summary>
        private void ApplyConfiguredPathsToSyspath()
        {
            try
            {
                var finalPaths = GetConfiguredPythonPaths();
                if (finalPaths.Any())
                    AddPathsToPythonSearchPath(finalPaths);
            }
            catch (Exception ex)
            {
            }
        }

        private List<string> GetConfiguredPythonPaths()
        {
            var paths = new List<string>();
            AddConfiguredPaths(paths);
            AddDetectedPaths(paths);

            return paths
                .Select(path => path.Trim())
                .Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private void AddConfiguredPaths(List<string> paths)
        {
            if (configuration.PythonPaths?.Any() != true)
                return;

            paths.AddRange(configuration.PythonPaths.Where(path => !string.IsNullOrWhiteSpace(path)));
        }

        private void AddDetectedPaths(List<string> paths)
        {
            if (!configuration.AutoDetectPythonPaths)
                return;

            if (!string.IsNullOrWhiteSpace(detectedEnv.PythonPaths))
            {
                paths.AddRange(detectedEnv.PythonPaths.Split(
                    Path.PathSeparator,
                    StringSplitOptions.RemoveEmptyEntries));
            }

            AddDetectedHomePaths(paths, detectedEnv.PythonHome);
        }

        private static void AddDetectedHomePaths(List<string> paths, string? pythonHome)
        {
            if (string.IsNullOrWhiteSpace(pythonHome))
                return;

            var library = Path.Combine(pythonHome, "Lib");
            foreach (var path in new[]
                     {
                         library,
                         Path.Combine(library, "site-packages"),
                         Path.Combine(pythonHome, "DLLs")
                     })
            {
                if (Directory.Exists(path))
                    paths.Add(path);
            }
        }

        private static void AddPathsToPythonSearchPath(IEnumerable<string> paths)
        {
            lock (gilExecutionLock)
            {
                using (Py.GIL())
                {
                    var currentPath = Py.Import("sys").GetAttr("path");
                    foreach (var path in paths)
                        TryAddPythonPath(currentPath, path);
                }
            }
        }

        private static void TryAddPythonPath(PyObject currentPath, string path)
        {
            using var pythonPath = new PyString(path);
            try
            {
                if (!currentPath.InvokeMethod("__contains__", pythonPath).As<bool>())
                    currentPath.InvokeMethod("insert", new PyInt(0), pythonPath);
            }
            catch
            {
                // A path that Python rejects must not prevent other configured paths from loading.
            }
        }

        private static string? RunPythonAndReadStdOut(string exe, string args)
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return null;
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(5000);
            return output;
        }

        public IDictionary<string, string> Execute(string code, IDictionary<string, string> inputs)
        {
            InitializePython();
            EnsurePackagesInstalled(code);

            // Serialize to avoid GIL deadlocks in ASP.NET environments
            lock (gilExecutionLock)
            {
                return ExecuteInGil(code, inputs);
            }
        }

        public ScriptExecutionResult ExecuteScript(string code, IDictionary<string, string> inputs)
        {
            // Delegate to Execute and wrap in ScriptExecutionResult
            var variables = Execute(code, inputs);

            // Serialize all output variables to JSON strings
            var result = new ScriptExecutionResult
            {
                OutputVariables = variables
                    .Where(kvp => !kvp.Key.StartsWith("error") && !kvp.Key.StartsWith("stackTrace"))
                    .ToDictionary(
                        kvp => kvp.Key,
                        kvp => kvp.Value), // pass-through; values are already JSON from ConvertFromPython
                StandardOutput = variables.ContainsKey("stdout") ? variables["stdout"]?.ToString() ?? string.Empty : string.Empty,
                StandardError = variables.ContainsKey("stderr") ? variables["stderr"]?.ToString() ?? string.Empty : string.Empty
            };

            // Check for errors
            if (variables.ContainsKey("error"))
            {
                result.Success = false;
                result.Error = new ScriptErrorInfo
                {
                    ErrorType = "PythonError",
                    Message = variables["error"]?.ToString() ?? "Unknown error",
                    StackTrace = variables.ContainsKey("stackTrace") ? variables["stackTrace"]?.ToString() ?? string.Empty : string.Empty
                };
            }
            else
            {
                result.Success = true;
            }

            return result;
        }

        private IDictionary<string, string> ExecuteInGil(string code, IDictionary<string, string> inputs)
        {
            var variables = new Dictionary<string, string>();
            outputBuffer.Clear();

            using (Py.GIL())
            using (var scope = Py.CreateScope())
            {
                try
                {
                    // Capture stdout/stderr
                    SetupOutputCapture(scope);

                    // Push inputs
                    PushInputs(scope, inputs);

                    // Execute
                    scope.Exec(code);

                    // Emit captured output
                    RecordCapturedOutput(scope, variables);
                    CollectVariables(scope, variables);
                }
                catch (PythonException ex)
                {
                    variables["error"] = ex.Message;
                    variables["stackTrace"] = ex.StackTrace ?? string.Empty;

                    var errorMessage = $"Python Error: {ex.Message}";
                    OutputCallback?.Invoke(errorMessage);
                    throw new Exception(errorMessage, ex);
                }
                catch (Exception ex)
                {
                    variables["error"] = ex.Message;
                    variables["stackTrace"] = ex.StackTrace ?? string.Empty;

                    var errorMessage = $"Execution Error: {ex.Message}";
                    OutputCallback?.Invoke(errorMessage);
                    throw;
                }
            }

            return variables;
        }

        private void PushInputs(PyModule scope, IDictionary<string, string> inputs)
        {
            foreach (var input in inputs)
            {
                using var pythonValue = ConvertToPython(input.Value);
                scope.Set(input.Key, pythonValue);
            }
        }

        private void RecordCapturedOutput(PyModule scope, IDictionary<string, string> variables)
        {
            var output = GetCapturedOutput(scope);
            if (string.IsNullOrEmpty(output))
                return;

            OutputCallback?.Invoke(output);
            if (!ContainsErrorIndicator(output))
                return;

            variables["error"] = "Script execution produced errors";
            variables["errorOutput"] = output;
        }

        private static bool ContainsErrorIndicator(string output) =>
            output.Contains("ERROR:")
            || output.Contains("Traceback")
            || output.Contains("Exception:")
            || output.Contains("Error:");

        private void CollectVariables(PyModule scope, IDictionary<string, string> variables)
        {
            var names = scope.Dir();
            for (var index = 0; index < names.Length(); index++)
            {
                var name = names[index].As<string>();
                if (IsInternalVariable(name))
                    continue;

                variables[name] = ReadVariable(scope, name);
            }
        }

        private static bool IsInternalVariable(string name) =>
            name.StartsWith("__")
            || name.StartsWith("_captured_")
            || name.Equals("sys")
            || name.Equals("StringIO")
            || name.Equals("_original_stdout")
            || name.Equals("_original_stderr")
            || name.Equals("_output_content");

        private string ReadVariable(PyModule scope, string name)
        {
            try
            {
                var pythonValue = scope.Get(name);
                return pythonValue == null ? string.Empty : ConvertFromPython(pythonValue);
            }
            catch
            {
                var pythonValue = scope.Get(name);
                return pythonValue?.ToString() ?? string.Empty;
            }
        }

        private void SetupOutputCapture(PyModule scope)
        {
            scope.Exec(@"
import sys
from io import StringIO
_captured_output = StringIO()
_original_stdout = sys.stdout
_original_stderr = sys.stderr
sys.stdout = _captured_output
sys.stderr = _captured_output
");
        }

        private string GetCapturedOutput(PyModule scope)
        {
            try
            {
                scope.Exec(@"
sys.stdout = _original_stdout
sys.stderr = _original_stderr
_output_content = _captured_output.getvalue()
_captured_output.close()
");

                var output = scope.Get("_output_content");
                return output?.As<string>() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private PyObject ConvertToPython(object value)
        {
            if (value == null)
                return PyObject.None;

            return value switch
            {
                string s => new PyString(s),
                int i => new PyInt(i),
                long l => new PyInt(l),
                double d => new PyFloat(d),
                float f => new PyFloat(f),
                bool b => b.ToPython(),
                byte[] bytes => bytes.ToPython(),
                IDictionary<string, object> dict => ConvertDictToPython(dict),
                IEnumerable<object> list => ConvertListToPython(list),
                _ => new PyString(value.ToString() ?? string.Empty)
            };
        }

        private PyObject ConvertDictToPython(IDictionary<string, object> dict)
        {
            var pyDict = new PyDict();
            foreach (var kvp in dict)
            {
                using var key = new PyString(kvp.Key);
                using var value = ConvertToPython(kvp.Value);
                pyDict.SetItem(key, value);
            }
            return pyDict;
        }

        private PyObject ConvertListToPython(IEnumerable<object> list)
        {
            var pyList = new PyList();
            foreach (var item in list)
            {
                using var pyItem = ConvertToPython(item);
                pyList.Append(pyItem);
            }
            return pyList;
        }

        /// <summary>
        /// Converts a Python object to a string representation that is valid JSON.
        /// Uses Python's json.dumps to ensure double-quoted JSON for lists/dicts/strings/primitives.
        /// Falls back to JSON-encoding of the object's string representation.
        /// </summary>
        private string ConvertFromPython(PyObject pyObj)
        {
            if (pyObj.IsNone())
                return "null";

            try
            {
                using var json = Py.Import("json");
                using var dumped = json.InvokeMethod("dumps", pyObj);
                return dumped.As<string>() ?? "null";
            }
            catch
            {
                try
                {
                    var s = pyObj.ToString() ?? string.Empty;
                    return JsonConvert.SerializeObject(s);
                }
                catch
                {
                    return "null";
                }
            }
        }

        public void Dispose()
        {
            OutputCallback = null;
            outputBuffer.Clear();
            // Do not call PythonEngine.Shutdown() here; it's global and may be used elsewhere in the process
        }

        /// <summary>
        /// Parses import statements from the script and installs any packages that are not
        /// already available in the Python environment. Uses a static cache to avoid re-checking
        /// packages that were already verified during the lifetime of the process.
        /// pip install runs out-of-process (no GIL held) with a 60-second timeout per package.
        /// </summary>
        private void EnsurePackagesInstalled(string code)
        {
            var importedModules = ImportRegex
                .Matches(code)
                .Select(m => m.Groups[1].Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(m => !StandardLibraryModules.Contains(m))
                .ToList();

            if (importedModules.Count == 0) return;

            var unchecked_ = importedModules.Where(m => !_verifiedModules.Contains(m)).ToList();
            if (unchecked_.Count == 0) return;

            lock (packageInstallLock)
            {
                // Re-check after acquiring lock (another thread may have just installed)
                var toProcess = unchecked_.Where(m => !_verifiedModules.Contains(m)).ToList();
                if (toProcess.Count == 0) return;

                foreach (var module in toProcess)
                {
                    // Skip if already importable (pre-installed in Dockerfile or installed earlier)
                    if (IsModuleAvailable(module))
                    {
                        _verifiedModules.Add(module);
                        continue;
                    }

                    var pipPackage = ImportToPipName.TryGetValue(module, out var mapped) ? mapped : module;
                    OutputCallback?.Invoke($"[AutoInstall] Installing missing package: {pipPackage}");

                    if (TryInstallPackage(pipPackage))
                    {
                        OutputCallback?.Invoke($"[AutoInstall] Successfully installed: {pipPackage}");
                    }
                    else
                    {
                        OutputCallback?.Invoke($"[AutoInstall] Could not install: {pipPackage} — script may fail if this import is required");
                    }

                    // Mark as processed regardless — avoid retrying on every execution
                    _verifiedModules.Add(module);
                }
            }
        }

        /// <summary>
        /// Checks whether a Python module is importable by running a quick out-of-process check.
        /// Avoids acquiring the GIL, keeping this safe to call before the execution lock.
        /// </summary>
        private bool IsModuleAvailable(string moduleName)
        {
            try
            {
                var pythonExe = detectedEnv.PythonExe ?? "python3";
                var psi = new ProcessStartInfo
                {
                    FileName = pythonExe,
                    Arguments = $"-c \"import importlib.util, sys; sys.exit(0 if importlib.util.find_spec('{moduleName}') is not None else 1)\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                using var proc = Process.Start(psi);
                if (proc == null) return false;
                proc.WaitForExit(10_000);
                return proc.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Runs pip install for the given package name. Returns true if the exit code is 0.
        /// </summary>
        private bool TryInstallPackage(string packageName)
        {
            try
            {
                var pythonExe = detectedEnv.PythonExe ?? "python3";
                var psi = new ProcessStartInfo
                {
                    FileName = pythonExe,
                    Arguments = $"-m pip install --quiet --disable-pip-version-check {packageName}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                using var proc = Process.Start(psi);
                if (proc == null) return false;
                proc.WaitForExit(60_000);
                return proc.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
