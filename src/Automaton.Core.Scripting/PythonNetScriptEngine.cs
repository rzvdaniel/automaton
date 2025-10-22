using Python.Runtime;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Automaton.Core.Scripting
{
    public class PythonNetScriptEngine : IScriptEngine, IOutputCallbackScriptEngine, IDisposable
    {
        private readonly object initLock = new object();
        private static readonly object gilExecutionLock = new object();
        private readonly StringBuilder outputBuffer = new StringBuilder();
        private readonly ScriptEngineConfiguration configuration;
        private IntPtr _allowThreadsState = IntPtr.Zero;

        // Extracted environment detection/configuration
        private readonly PythonEnvironmentInfo detectedEnv;
        private readonly PythonEnvironmentDetector detector;
        private readonly PythonEnvironmentConfigurator configurator;

        public Action<string>? OutputCallback { get; set; }

        public PythonNetScriptEngine() : this(new ScriptEngineConfiguration())
        {
        }

        public PythonNetScriptEngine(ScriptEngineConfiguration configuration)
        {
            this.configuration = configuration;
            this.detector = new PythonEnvironmentDetector();
            this.configurator = new PythonEnvironmentConfigurator();
            this.detectedEnv = detector.Detect(configuration.EnableDebugMode);
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
                            // Priority 2: Auto-detect and set DLL
                            if (configuration.EnableDebugMode)
                            {
                                Console.WriteLine("[DEBUG] Auto-detecting Python DLL...");
                            }
                            configurator.Configure(detectedEnv, configuration, configuration.EnableDebugMode);
                            if (configuration.EnableDebugMode)
                            {
                                Console.WriteLine($"[DEBUG] Auto-detected Runtime.PythonDLL: {Runtime.PythonDLL}");
                            }
                        }

                        if (configuration.EnableDebugMode)
                        {
                            Console.WriteLine($"[DEBUG] Final Runtime.PythonDLL before Initialize(): {Runtime.PythonDLL}");
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
                    if (configuration.EnableDebugMode)
                    {
                        Console.WriteLine($"[ERROR] Python.NET initialization failed: {ex.Message}");
                        Console.WriteLine($"[ERROR] InnerException: {ex.InnerException?.Message}");
                        Console.WriteLine($"[ERROR] StackTrace: {ex.StackTrace}");
                        if (!string.IsNullOrWhiteSpace(Runtime.PythonDLL))
                        {
                            Console.WriteLine($"[ERROR] Runtime.PythonDLL: {Runtime.PythonDLL}");
                            Console.WriteLine($"[ERROR] DLL exists: {System.IO.File.Exists(Runtime.PythonDLL)}");
                        }
                    }
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
                var pathsToAdd = new List<string>();

                // Priority 1: Add user-configured paths first
                if (configuration.PythonPaths?.Any() == true)
                {
                    foreach (var p in configuration.PythonPaths.Where(p => !string.IsNullOrWhiteSpace(p)))
                    {
                        pathsToAdd.Add(p);
                        if (configuration.EnableDebugMode)
                        {
                            Console.WriteLine($"Adding user-configured Python path: {p}");
                        }
                    }
                }

                // Priority 2: ALSO add auto-detected paths if auto-detection is enabled
                // This is now additive: both user and auto-detected paths are combined
                if (configuration.AutoDetectPythonPaths)
                {
                    if (!string.IsNullOrWhiteSpace(detectedEnv.PythonPaths))
                    {
                        var detectedPaths = detectedEnv.PythonPaths.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var p in detectedPaths)
                        {
                            pathsToAdd.Add(p);
                            if (configuration.EnableDebugMode)
                            {
                                Console.WriteLine($"Adding auto-detected Python path: {p}");
                            }
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(detectedEnv.PythonHome))
                    {
                        var lib = Path.Combine(detectedEnv.PythonHome, "Lib");
                        var sitePkgs = Path.Combine(lib, "site-packages");
                        var dlls = Path.Combine(detectedEnv.PythonHome, "DLLs");
                        foreach (var p in new[] { lib, sitePkgs, dlls })
                        {
                            if (Directory.Exists(p))
                            {
                                pathsToAdd.Add(p);
                                if (configuration.EnableDebugMode)
                                {
                                    Console.WriteLine($"Adding auto-detected Python path: {p}");
                                }
                            }
                        }
                    }
                }

                // Deduplicate and filter - removes duplicates and non-existent paths
                var finalPaths = pathsToAdd
                    .Select(p => p.Trim())
                    .Where(p => !string.IsNullOrWhiteSpace(p) && Directory.Exists(p))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (finalPaths.Any())
                {
                    lock (gilExecutionLock)
                    {
                        using (Py.GIL())
                        {
                            var sys = Py.Import("sys");
                            var currentPath = sys.GetAttr("path");

                            foreach (var p in finalPaths)
                            {
                                using var pyPath = new PyString(p);
                                try
                                {
                                    if (!currentPath.InvokeMethod("__contains__", pyPath).As<bool>())
                                    {
                                        currentPath.InvokeMethod("insert", new PyInt(0), pyPath);
                                        if (configuration.EnableDebugMode)
                                        {
                                            Console.WriteLine($"? Added to sys.path: {p}");
                                        }
                                    }
                                }
                                catch { }
                            }
                        }
                    }

                    if (configuration.EnableDebugMode)
                    {
                        Console.WriteLine($"Successfully configured {finalPaths.Count} Python paths");
                    }
                }
            }
            catch (Exception ex)
            {
                if (configuration.EnableDebugMode)
                {
                    Console.WriteLine($"Warning: Failed to apply configured paths to sys.path: {ex.Message}");
                }
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