using Python.Runtime;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Automaton.Core.Scripting
{
    public class PythonNetScriptEngine : IScriptEngine, IOutputCallbackScriptEngine, IDisposable
    {
        private bool isInitialized = false;
        private readonly object initLock = new object();
        private static readonly object gilExecutionLock = new object();
        private readonly StringBuilder outputBuffer = new StringBuilder();
        private readonly ScriptEngineConfiguration configuration;
        private IntPtr _allowThreadsState = IntPtr.Zero;

        // Detected Python environment (used to resolve external packages like 'requests')
        private string? _detectedPythonHome;
        private string? _detectedPythonPaths; // os.pathsep-joined list
        private string? _detectedPythonExe;   // Path to the python executable
        private int? _detectedPyMajor;
        private int? _detectedPyMinor;

        public Action<string>? OutputCallback { get; set; }

        public PythonNetScriptEngine() : this(new ScriptEngineConfiguration())
        {
        }

        public PythonNetScriptEngine(ScriptEngineConfiguration configuration)
        {
            this.configuration = configuration;
            InitializePython();
        }

        private void InitializePython()
        {
            lock (initLock)
            {
                if (isInitialized || PythonEngine.IsInitialized)
                {
                    isInitialized = true;
                    return;
                }

                try
                {
                    // Priority 1: Use user-specified Python DLL path from configuration
                    if (!string.IsNullOrWhiteSpace(configuration.PythonDllPath))
                    {
                        Runtime.PythonDLL = configuration.PythonDllPath;
                        if (configuration.EnableDebugMode)
                        {
                            Console.WriteLine($"Using user-specified Python DLL: {configuration.PythonDllPath}");
                        }
                    }
                    else
                    {
                        // Priority 2: Auto-detect Python environment if no user-specified DLL
                        if (configuration.AutoDetectPythonPaths)
                        {
                            DetectPythonEnvironment();
                            TryResolveAndSetPythonDll();
                        }
                    }

                    // Apply PYTHONHOME/PYTHONPATH before initializing runtime
                    // This respects user-configured paths with priority over auto-detection
                    ApplyHostPythonEnvironmentVariables();

                    // Initialize Python and release the GIL so ASP.NET threads are not blocked
                    PythonEngine.Initialize();
                    _allowThreadsState = PythonEngine.BeginAllowThreads();
                    isInitialized = true;

                    // Also inject configured paths into sys.path for the running interpreter
                    EnsureSysPathContainsConfiguredPaths();
                }
                catch (Exception ex)
                {
                    if (configuration.EnableDebugMode)
                    {
                        Console.WriteLine($"Python.NET initialization failed: {ex.Message}");
                        if (!string.IsNullOrWhiteSpace(Runtime.PythonDLL))
                        {
                            Console.WriteLine($"Runtime.PythonDLL: {Runtime.PythonDLL}");
                        }
                    }
                    throw;
                }
            }
        }

        // Best-effort: query a system Python to get exec_prefix and sys.path
        private void DetectPythonEnvironment()
        {
            try
            {
                var candidates = new[] { "python3", "python" };
                foreach (var exe in candidates)
                {
                    try
                    {
                        var home = RunPythonAndReadStdOut(exe, "-c \"import sys; print(sys.exec_prefix)\"")?.Trim();
                        if (string.IsNullOrWhiteSpace(home) || !Directory.Exists(home))
                            continue;

                        var paths = RunPythonAndReadStdOut(exe, "-c \"import sys, os; print(os.pathsep.join(sys.path))\"")?.Trim();
                        if (string.IsNullOrWhiteSpace(paths))
                            continue;

                        var ver = RunPythonAndReadStdOut(exe, "-c \"import sys; print(f'{sys.version_info.major}.{sys.version_info.minor}')\"")?.Trim();
                        var sysExe = RunPythonAndReadStdOut(exe, "-c \"import sys; print(sys.executable)\"")?.Trim();

                        _detectedPythonHome = home;
                        _detectedPythonPaths = paths;
                        _detectedPythonExe = !string.IsNullOrWhiteSpace(sysExe) ? sysExe : null;

                        if (!string.IsNullOrWhiteSpace(ver))
                        {
                            var parts = ver.Split('.', StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 2 && int.TryParse(parts[0], out var maj) && int.TryParse(parts[1], out var min))
                            {
                                _detectedPyMajor = maj;
                                _detectedPyMinor = min;
                            }
                        }

                        if (configuration.EnableDebugMode)
                        {
                            Console.WriteLine($"Detected Python home: {_detectedPythonHome}");
                            if (!string.IsNullOrWhiteSpace(_detectedPythonExe))
                                Console.WriteLine($"Detected Python exe: {_detectedPythonExe}");
                            if (_detectedPyMajor.HasValue)
                                Console.WriteLine($"Detected Python version: {_detectedPyMajor}.{_detectedPyMinor}");
                        }
                        break;
                    }
                    catch
                    {
                        // try next
                    }
                }
            }
            catch
            {
                // ignore
            }
        }

        private void TryResolveAndSetPythonDll()
        {
            try
            {
                // Only attempt on Windows; on Linux/macOS pythonnet uses libpython (different file names)
                if (!OperatingSystem.IsWindows()) return;

                // If user specified a DLL path in configuration, do not auto-resolve
                if (!string.IsNullOrWhiteSpace(configuration.PythonDllPath)) 
                {
                    if (configuration.EnableDebugMode)
                    {
                        Console.WriteLine("Skipping Python DLL auto-resolution (user-specified path is set)");
                    }
                    return;
                }

                // If already set elsewhere, do nothing
                if (!string.IsNullOrWhiteSpace(Runtime.PythonDLL)) return;

                var candidateDirs = new List<string>();

                // Detected locations
                if (!string.IsNullOrWhiteSpace(_detectedPythonHome) && Directory.Exists(_detectedPythonHome))
                {
                    candidateDirs.Add(_detectedPythonHome);
                    var dlls = Path.Combine(_detectedPythonHome, "DLLs");
                    var bin = Path.Combine(_detectedPythonHome, "bin");
                    if (Directory.Exists(dlls)) candidateDirs.Add(dlls);
                    if (Directory.Exists(bin)) candidateDirs.Add(bin);
                }

                if (!string.IsNullOrWhiteSpace(_detectedPythonExe))
                {
                    var exeDir = Path.GetDirectoryName(_detectedPythonExe);
                    if (!string.IsNullOrWhiteSpace(exeDir)) candidateDirs.Add(exeDir);
                }

                // PYTHONHOME
                var pyHomeEnv = Environment.GetEnvironmentVariable("PYTHONHOME");
                if (!string.IsNullOrWhiteSpace(pyHomeEnv) && Directory.Exists(pyHomeEnv))
                {
                    candidateDirs.Add(pyHomeEnv);
                    var dlls = Path.Combine(pyHomeEnv, "DLLs");
                    var bin = Path.Combine(pyHomeEnv, "bin");
                    if (Directory.Exists(dlls)) candidateDirs.Add(dlls);
                    if (Directory.Exists(bin)) candidateDirs.Add(bin);
                }

                // PATH entries
                var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                foreach (var p in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (Directory.Exists(p)) candidateDirs.Add(p);
                }

                // De-duplicate
                candidateDirs = candidateDirs
                    .Select(p => p.Trim())
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                string? bestMatch = null;
                int bestScore = int.MinValue;

                // Build expected version token, e.g., 311, 312
                var versionToken = (_detectedPyMajor.HasValue && _detectedPyMinor.HasValue)
                    ? ($"{_detectedPyMajor}{_detectedPyMinor}")
                    : null;

                foreach (var dir in candidateDirs)
                {
                    try
                    {
                        // search for python3*.dll first, then python*.dll as fallback
                        var files = Enumerable.Empty<string>();
                        try { files = Directory.EnumerateFiles(dir, "python3*.dll"); } catch { }
                        if (!files.Any())
                        {
                            try { files = Directory.EnumerateFiles(dir, "python*.dll"); } catch { }
                        }

                        foreach (var file in files)
                        {
                            var name = Path.GetFileName(file);
                            // Typical names: python311.dll, python312.dll
                            var m = Regex.Match(name, @"python(?<maj>\d)(?<min>\d{1,2})\.dll", RegexOptions.IgnoreCase);
                            int score = 0;
                            if (m.Success)
                            {
                                var token = m.Groups["maj"].Value + m.Groups["min"].Value; // e.g. 311
                                if (!string.IsNullOrWhiteSpace(versionToken))
                                {
                                    if (string.Equals(token, versionToken, StringComparison.Ordinal))
                                    {
                                        score += 1000; // exact match
                                    }
                                    else if (token.StartsWith(_detectedPyMajor?.ToString() ?? string.Empty))
                                    {
                                        score += 500; // same major
                                    }
                                }
                            }
                            else
                            {
                                // Unknown pattern, lower confidence
                                score += 10;
                            }

                            // Prefer files closer to detected locations by giving base dir a boost
                            if (!string.IsNullOrWhiteSpace(_detectedPythonHome) && dir.StartsWith(_detectedPythonHome, StringComparison.OrdinalIgnoreCase))
                            {
                                score += 50;
                            }

                            if (score > bestScore)
                            {
                                bestScore = score;
                                bestMatch = file;
                            }
                        }
                    }
                    catch
                    {
                        // ignore this directory
                    }
                }

                if (!string.IsNullOrWhiteSpace(bestMatch) && File.Exists(bestMatch))
                {
                    Runtime.PythonDLL = bestMatch;
                    if (configuration.EnableDebugMode)
                    {
                        Console.WriteLine($"Auto-resolved Runtime.PythonDLL to: {bestMatch}");
                    }
                }
            }
            catch
            {
                // swallow, pythonnet will throw if not set and not discoverable
            }
        }

        private void ApplyHostPythonEnvironmentVariables()
        {
            try
            {
                var pathList = new List<string>();

                // Priority 1: Add user-configured Python paths first
                if (configuration.PythonPaths?.Any() == true)
                {
                    foreach (var p in configuration.PythonPaths.Where(p => !string.IsNullOrWhiteSpace(p)))
                    {
                        pathList.Add(p);
                        if (configuration.EnableDebugMode)
                        {
                            Console.WriteLine($"Adding user-specified Python path: {p}");
                        }
                    }
                }

                // Priority 2: Add auto-detected paths if auto-detection is enabled
                if (configuration.AutoDetectPythonPaths)
                {
                    if (!string.IsNullOrWhiteSpace(_detectedPythonPaths))
                    {
                        pathList.AddRange(_detectedPythonPaths.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));
                    }

                    if (!string.IsNullOrWhiteSpace(_detectedPythonHome))
                    {
                        var lib = Path.Combine(_detectedPythonHome, "Lib");
                        var sitePkgs = Path.Combine(lib, "site-packages");
                        var dlls = Path.Combine(_detectedPythonHome, "DLLs");
                        foreach (var p in new[] { lib, sitePkgs, dlls })
                        {
                            if (Directory.Exists(p)) pathList.Add(p);
                        }
                        // help stdlib resolution
                        Environment.SetEnvironmentVariable("PYTHONHOME", _detectedPythonHome);
                    }
                }

                var finalPaths = pathList
                    .Select(p => p.Trim())
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct()
                    .ToArray();

                if (finalPaths.Length > 0)
                {
                    var joined = string.Join(Path.PathSeparator, finalPaths);
                    Environment.SetEnvironmentVariable("PYTHONPATH", joined);
                    if (configuration.EnableDebugMode)
                    {
                        Console.WriteLine($"Set PYTHONPATH with {finalPaths.Length} paths (user-specified paths take priority)");
                        Console.WriteLine($"PYTHONPATH: {joined}");
                    }
                }
            }
            catch
            {
                // ignore
            }
        }
        private void EnsureSysPathContainsConfiguredPaths()
        {
            try
            {
                var envPyPath = Environment.GetEnvironmentVariable("PYTHONPATH");
                if (string.IsNullOrWhiteSpace(envPyPath)) return;
                var parts = envPyPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) return;

                lock (gilExecutionLock)
                {
                    using (Py.GIL())
                    {
                        var sys = Py.Import("sys");
                        var currentPath = sys.GetAttr("path");
                        foreach (var p in parts)
                        {
                            using var pyPath = new PyString(p);
                            try
                            {
                                if (!currentPath.InvokeMethod("__contains__", pyPath).As<bool>())
                                {
                                    currentPath.InvokeMethod("append", pyPath);
                                }
                            }
                            catch { }
                        }
                    }
                }
            }
            catch { }
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

        public IDictionary<string, object> Execute(string code, IDictionary<string, object> inputs)
        {
            InitializePython();

            // Serialize to avoid GIL deadlocks in ASP.NET environments
            lock (gilExecutionLock)
            {
                return ExecuteInGil(code, inputs);
            }
        }

        public ScriptExecutionResult ExecuteScript(string code, IDictionary<string, object> inputs)
        {
            // Delegate to Execute and wrap in ScriptExecutionResult
            var variables = Execute(code, inputs);
            
            var result = new ScriptExecutionResult
            {
                OutputVariables = variables
                    .Where(kvp => !kvp.Key.StartsWith("error") && !kvp.Key.StartsWith("stackTrace"))
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
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

        private IDictionary<string, object> ExecuteInGil(string code, IDictionary<string, object> inputs)
        {
            var variables = new Dictionary<string, object>();
            outputBuffer.Clear();

            using (Py.GIL())
            using (var scope = Py.CreateScope())
            {
                try
                {
                    // Capture stdout/stderr
                    SetupOutputCapture(scope);

                    // Push inputs
                    foreach (var kvp in inputs)
                    {
                        using var pyValue = ConvertToPython(kvp.Value);
                        scope.Set(kvp.Key, pyValue);
                    }

                    // Execute
                    scope.Exec(code);

                    // Emit captured output
                    var capturedOutput = GetCapturedOutput(scope);
                    if (!string.IsNullOrEmpty(capturedOutput))
                    {
                        OutputCallback?.Invoke(capturedOutput);
                        
                        // Check if the output contains error indicators
                        if (capturedOutput.Contains("ERROR:") || 
                            capturedOutput.Contains("Traceback") || 
                            capturedOutput.Contains("Exception:") ||
                            capturedOutput.Contains("Error:"))
                        {
                            variables["error"] = "Script execution produced errors";
                            variables["errorOutput"] = capturedOutput;
                        }
                    }

                    // Collect variables (skip builtins and our helpers)
                    var namesObj = scope.Dir();
                    for (int i = 0; i < namesObj.Length(); i++)
                    {
                        var name = namesObj[i].As<string>();
                        if (!name.StartsWith("__") &&
                            !name.StartsWith("_captured_") &&
                            !name.Equals("sys") &&
                            !name.Equals("StringIO") &&
                            !name.Equals("_original_stdout") &&
                            !name.Equals("_original_stderr") &&
                            !name.Equals("_output_content"))
                        {
                            try
                            {
                                var pyValue = scope.Get(name);
                                if (pyValue != null)
                                {
                                    var value = ConvertFromPython(pyValue);
                                    variables[name] = value;
                                }
                            }
                            catch
                            {
                                var pyValue = scope.Get(name);
                                variables[name] = pyValue?.ToString() ?? string.Empty;
                            }
                        }
                    }
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

        private object ConvertFromPython(PyObject pyObj)
        {
            if (pyObj.IsNone())
                return null!;

            try
            {
                if (pyObj.GetPythonType().Name == "bool")
                {
                    return pyObj.As<bool>();
                }
            }
            catch { }

            if (PyString.IsStringType(pyObj))
                return pyObj.As<string>();

            if (PyInt.IsIntType(pyObj))
            {
                try { return pyObj.As<long>(); } catch { }
                try { return pyObj.As<int>(); } catch { }
            }

            if (PyFloat.IsFloatType(pyObj))
            {
                try { return pyObj.As<double>(); } catch { }
            }

            if (pyObj.IsIterable() && !PyString.IsStringType(pyObj))
            {
                if (pyObj.HasAttr("keys"))
                {
                    var dict = new Dictionary<string, object>();
                    try
                    {
                        var keys = pyObj.GetAttr("keys").Invoke();
                        for (int i = 0; i < keys.Length(); i++)
                        {
                            var key = keys[i];
                            var keyStr = key.As<string>();
                            var value = pyObj.GetItem(key);
                            dict[keyStr] = ConvertFromPython(value);
                        }
                        return dict;
                    }
                    catch { }
                }

                try
                {
                    var list = new List<object>();
                    for (int i = 0; i < pyObj.Length(); i++)
                    {
                        list.Add(ConvertFromPython(pyObj[i]));
                    }
                    return list;
                }
                catch { }
            }

            return pyObj.ToString() ?? string.Empty;
        }

        public void Dispose()
        {
            OutputCallback = null;
            outputBuffer.Clear();
            // Do not call PythonEngine.Shutdown() here; it's global and may be used elsewhere in the process
        }
    }
}