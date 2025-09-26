using Python.Runtime;
using System.Collections.Generic;
using System.Text;
using System.Runtime.InteropServices;
using System.Diagnostics;

namespace Automaton.Core.Scripting
{
    public class PythonNetScriptEngine : IScriptEngine, IOutputCallbackScriptEngine, IDisposable
    {
        private bool isInitialized = false;
        private readonly object lockObject = new object();
        private StringBuilder outputBuffer = new StringBuilder();
        private readonly ScriptEngineConfiguration configuration;
        
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
            lock (lockObject)
            {
                if (!isInitialized && !PythonEngine.IsInitialized)
                {
                    try
                    {
                        // Try to set up Python DLL path if specified
                        if (!string.IsNullOrEmpty(configuration.PythonDllPath))
                        {
                            Runtime.PythonDLL = configuration.PythonDllPath;
                        }
                        else
                        {
                            // Try to auto-detect Python installation
                            TryAutoDetectPythonDll();
                        }

                        // Initialize Python engine
                        PythonEngine.Initialize();
                        
                        // Configure Python module paths after initialization
                        ConfigurePythonPaths();
                        
                        isInitialized = true;
                    }
                    catch (Exception ex)
                    {
                        if (configuration.EnableDebugMode)
                        {
                            Console.WriteLine($"Python.NET initialization failed: {ex.Message}");
                        }
                        throw;
                    }
                }
            }
        }

        private void TryAutoDetectPythonDll()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Try to get Python info dynamically first
                var pythonInfo = GetPythonInfoFromExecutable();
                if (pythonInfo != null)
                {
                    Runtime.PythonDLL = pythonInfo.DllPath;
                    if (configuration.EnableDebugMode)
                    {
                        Console.WriteLine($"? Found Python DLL via executable detection: {pythonInfo.DllPath}");
                    }
                    return;
                }

                // Fallback to hardcoded paths
                var possiblePaths = new[]
                {
                    @"C:\Python311\python311.dll",
                    @"C:\Python310\python310.dll",
                    @"C:\Python39\python39.dll",
                    @"C:\Python38\python38.dll",
                    Environment.GetEnvironmentVariable("PYTHONHOME") + @"\python311.dll",
                    Environment.GetEnvironmentVariable("PYTHONHOME") + @"\python310.dll",
                    Environment.GetEnvironmentVariable("PYTHONHOME") + @"\python39.dll",
                    Environment.GetEnvironmentVariable("PYTHONHOME") + @"\python38.dll"
                };

                foreach (var path in possiblePaths.Where(p => !string.IsNullOrEmpty(p)))
                {
                    if (File.Exists(path))
                    {
                        Runtime.PythonDLL = path;
                        if (configuration.EnableDebugMode)
                        {
                            Console.WriteLine($"? Found Python DLL at hardcoded path: {path}");
                        }
                        return;
                    }
                }

                // Additional fallback: Try to detect Python from PATH and derive DLL location
                var pathBasedPythonDll = DetectPythonDllFromPath();
                if (!string.IsNullOrEmpty(pathBasedPythonDll))
                {
                    Runtime.PythonDLL = pathBasedPythonDll;
                    if (configuration.EnableDebugMode)
                    {
                        Console.WriteLine($"? Found Python DLL via PATH detection: {pathBasedPythonDll}");
                    }
                    return;
                }
            }
            else
            {
                // For Linux/macOS, try to get Python info dynamically
                var pythonInfo = GetPythonInfoFromExecutable();
                if (pythonInfo != null && !string.IsNullOrEmpty(pythonInfo.DllPath))
                {
                    Runtime.PythonDLL = pythonInfo.DllPath;
                    if (configuration.EnableDebugMode)
                    {
                        Console.WriteLine($"? Found Python shared library: {pythonInfo.DllPath}");
                    }
                    return;
                }
                
                // For Linux/macOS, Python.NET usually works with system Python installations
                // even without explicitly setting the DLL path
                if (configuration.EnableDebugMode)
                {
                    Console.WriteLine("? Python.NET will attempt to use system Python installation");
                }
            }

            if (configuration.EnableDebugMode)
            {
                Console.WriteLine("? No Python installation detected. Python.NET may fail to initialize.");
            }
        }

        /// <summary>
        /// Simplified Python path detection as fallback method
        /// </summary>
        private string? DetectPythonDllFromPath()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return null;

            string[] candidates = { "python3", "python" };

            foreach (var exe in candidates)
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = exe,
                        Arguments = "-c \"import sys, os; print(sys.exec_prefix)\"",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    
                    using var proc = Process.Start(psi);
                    if (proc == null) continue;
                    
                    string output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit();

                    if (proc.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                    {
                        var pythonHome = output.Trim();
                        
                        // Try to get Python version for DLL name
                        var versionPsi = new ProcessStartInfo
                        {
                            FileName = exe,
                            Arguments = "-c \"import sys; print(f'{sys.version_info.major}{sys.version_info.minor}')\"",
                            RedirectStandardOutput = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        
                        using var versionProc = Process.Start(versionPsi);
                        if (versionProc == null) continue;
                        
                        string versionOutput = versionProc.StandardOutput.ReadToEnd();
                        versionProc.WaitForExit();
                        
                        if (versionProc.ExitCode == 0 && !string.IsNullOrWhiteSpace(versionOutput))
                        {
                            var version = versionOutput.Trim();
                            var dllName = $"python{version}.dll";
                            var dllPath = Path.Combine(pythonHome, dllName);
                            
                            if (File.Exists(dllPath))
                            {
                                return dllPath;
                            }
                            
                            // Try DLLs subdirectory
                            var dllsDirPath = Path.Combine(pythonHome, "DLLs", dllName);
                            if (File.Exists(dllsDirPath))
                            {
                                return dllsDirPath;
                            }
                        }
                    }
                }
                catch
                {
                    // This candidate not found, try next
                    continue;
                }
            }

            return null; // No python found via PATH
        }

        private PythonInfo? GetPythonInfoFromExecutable()
        {
            string[] candidates = { "python3", "python" };

            foreach (var exe in candidates)
            {
                try
                {
                    // Get Python paths for module resolution
                    var pathInfo = GetPythonPaths(exe);
                    if (pathInfo == null) continue;

                    // Get Python DLL path
                    var dllPath = GetPythonDllPath(exe);
                    
                    return new PythonInfo
                    {
                        Executable = exe,
                        ModulePaths = pathInfo,
                        DllPath = dllPath
                    };
                }
                catch
                {
                    // This candidate not found, try next
                    continue;
                }
            }

            return null; // No python found
        }

        /// <summary>
        /// Simplified Python paths detection utility method
        /// </summary>
        private static string? GetPythonPaths()
        {
            string[] candidates = { "python3", "python" };

            foreach (var exe in candidates)
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = exe,
                        Arguments = "-c \"import sys, os; print(os.pathsep.join(sys.path))\"",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var proc = Process.Start(psi);
                    if (proc == null) continue;
                    
                    string output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit();

                    if (proc.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                    {
                        return output.Trim();
                    }
                }
                catch
                {
                    // this candidate not found, try next
                }
            }

            return null; // no python found
        }

        private string? GetPythonPaths(string pythonExecutable)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = pythonExecutable,
                    Arguments = "-c \"import sys, os; print(os.pathsep.join(sys.path))\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                
                using var proc = Process.Start(psi);
                if (proc == null) return null;
                
                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();

                if (proc.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                {
                    return output.Trim();
                }
            }
            catch
            {
                // Failed to get paths from this Python executable
            }

            return null;
        }

        private string? GetPythonDllPath(string pythonExecutable)
        {
            try
            {
                string script;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    script = @"
import sys
import os
# For Windows, find the DLL in the Python installation directory
python_dll = f'python{sys.version_info.major}{sys.version_info.minor}.dll'
dll_path = os.path.join(sys.exec_prefix, python_dll)
if os.path.exists(dll_path):
    print(dll_path)
else:
    # Try in the DLLs directory
    dll_path = os.path.join(sys.exec_prefix, 'DLLs', python_dll)
    if os.path.exists(dll_path):
        print(dll_path)
";
                }
                else
                {
                    script = @"
import sys
import os
import sysconfig
# For Linux/macOS, find the shared library
version = f'{sys.version_info.major}.{sys.version_info.minor}'
lib_name = f'libpython{version}.so'
if sys.platform == 'darwin':
    lib_name = f'libpython{version}.dylib'

# Try common locations
lib_paths = [
    os.path.join(sysconfig.get_config_var('LIBDIR'), lib_name),
    os.path.join(sys.exec_prefix, 'lib', lib_name),
    f'/usr/lib/x86_64-linux-gnu/{lib_name}',
    f'/usr/lib/{lib_name}',
    f'/usr/local/lib/{lib_name}'
]

for path in lib_paths:
    if os.path.exists(path):
        print(path)
        break
";
                }

                var psi = new ProcessStartInfo
                {
                    FileName = pythonExecutable,
                    Arguments = $"-c \"{script}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                
                using var proc = Process.Start(psi);
                if (proc == null) return null;
                
                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();

                if (proc.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                {
                    var dllPath = output.Trim();
                    if (File.Exists(dllPath))
                    {
                        return dllPath;
                    }
                }
            }
            catch
            {
                // Failed to get DLL path from this Python executable
            }

            return null;
        }

        private void ConfigurePythonPaths()
        {
            if (PythonEngine.IsInitialized)
            {
                using (Py.GIL())
                {
                    try
                    {
                        // Get Python info to configure module paths
                        var pythonInfo = GetPythonInfoFromExecutable();
                        if (pythonInfo?.ModulePaths != null)
                        {
                            var sys = Py.Import("sys");
                            var currentPath = sys.GetAttr("path");
                            
                            // Add discovered Python paths to sys.path
                            var paths = pythonInfo.ModulePaths.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
                            foreach (var path in paths.Where(p => !string.IsNullOrWhiteSpace(p) && Directory.Exists(p)))
                            {
                                using (var pyPath = new PyString(path))
                                {
                                    if (!currentPath.InvokeMethod("__contains__", pyPath).As<bool>())
                                    {
                                        currentPath.InvokeMethod("append", pyPath);
                                    }
                                }
                            }
                        }
                        else
                        {
                            // Fallback: try the simplified utility method
                            var simplePaths = GetPythonPaths();
                            if (!string.IsNullOrEmpty(simplePaths))
                            {
                                var sys = Py.Import("sys");
                                var currentPath = sys.GetAttr("path");
                                
                                var paths = simplePaths.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
                                foreach (var path in paths.Where(p => !string.IsNullOrWhiteSpace(p) && Directory.Exists(p)))
                                {
                                    using (var pyPath = new PyString(path))
                                    {
                                        if (!currentPath.InvokeMethod("__contains__", pyPath).As<bool>())
                                        {
                                            currentPath.InvokeMethod("append", pyPath);
                                        }
                                    }
                                }
                            }
                        }

                        // Add configured additional paths
                        var sys2 = Py.Import("sys");
                        var currentPath2 = sys2.GetAttr("path");
                        foreach (var additionalPath in configuration.PythonPaths)
                        {
                            if (!string.IsNullOrWhiteSpace(additionalPath) && Directory.Exists(additionalPath))
                            {
                                using (var pyPath = new PyString(additionalPath))
                                {
                                    if (!currentPath2.InvokeMethod("__contains__", pyPath).As<bool>())
                                    {
                                        currentPath2.InvokeMethod("append", pyPath);
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        if (configuration.EnableDebugMode)
                        {
                            Console.WriteLine($"Warning: Failed to configure Python paths: {ex.Message}");
                        }
                    }
                }
            }
        }

        public IDictionary<string, object> Execute(string code, IDictionary<string, object> inputs)
        {
            InitializePython();
            
            // Determine if this is likely a network operation
            bool hasNetworkOperations = code.Contains("requests") || code.Contains("urllib") || code.Contains("http") || code.Contains("fetch");
            
            // Use different timeout strategies based on the type of operation
            var timeoutSeconds = hasNetworkOperations 
                ? Math.Min(configuration.NetworkTimeoutSeconds, 10)  // Short timeout for network operations
                : Math.Min(configuration.DefaultTimeoutSeconds, 30); // Longer timeout for regular operations
                
            var timeout = TimeSpan.FromSeconds(timeoutSeconds);
            
            // For simple operations, try direct execution first
            if (!hasNetworkOperations)
            {
                try
                {
                    return ExecutePythonCodeDirectly(code, inputs);
                }
                catch (Exception ex)
                {
                    if (configuration.EnableDebugMode)
                    {
                        Console.WriteLine($"Direct execution failed: {ex.Message}, falling back to timeout execution");
                    }
                    // Fall through to timeout execution
                }
            }
            
            // Use timeout execution for network operations or when direct execution fails
            using var cts = new CancellationTokenSource(timeout);
            
            try
            {
                // Create a task that can be cancelled
                var executeTask = Task.Run(() => 
                {
                    return ExecutePythonCodeWithTimeout(code, inputs);
                }, cts.Token);

                // Wait for completion or timeout
                if (executeTask.Wait(timeout, cts.Token))
                {
                    return executeTask.Result;
                }
                else
                {
                    // Task didn't complete in time
                    var errorMessage = $"Python execution timed out after {timeoutSeconds} seconds. " +
                                     (hasNetworkOperations 
                                        ? "This indicates network I/O blocking. Consider using shorter timeouts or simpler HTTP libraries."
                                        : "This may indicate a deadlock or infinite loop in the Python code.");
                    
                    if (configuration.EnableDebugMode)
                    {
                        Console.WriteLine($"? {errorMessage}");
                        if (hasNetworkOperations)
                        {
                            Console.WriteLine("?? For web applications, consider using libraries like 'httpx' with explicit async/await patterns.");
                        }
                    }
                    
                    throw new TimeoutException(errorMessage);
                }
            }
            catch (OperationCanceledException)
            {
                var errorMessage = $"Python execution was cancelled after {timeoutSeconds} seconds due to timeout.";
                if (configuration.EnableDebugMode)
                {
                    Console.WriteLine($"?? {errorMessage}");
                }
                throw new TimeoutException(errorMessage);
            }
            catch (AggregateException ex) when (ex.InnerException != null)
            {
                // Unwrap the actual exception
                throw ex.InnerException;
            }
        }

        private IDictionary<string, object> ExecutePythonCodeDirectly(string code, IDictionary<string, object> inputs)
        {
            var variables = new Dictionary<string, object>();
            outputBuffer.Clear();

            // Direct execution without timeout - for simple operations only
            using (Py.GIL())
            {
                using (var scope = Py.CreateScope())
                {
                    try
                    {
                        // Set up basic output capture
                        SetupOutputCapture(scope);

                        // Set input variables in Python scope
                        foreach (var kvp in inputs)
                        {
                            using (var pyValue = ConvertToPython(kvp.Value))
                            {
                                scope.Set(kvp.Key, pyValue);
                            }
                        }

                        // Execute code directly
                        scope.Exec(code);

                        // Get output from captured streams
                        var capturedOutput = GetCapturedOutput(scope);
                        if (!string.IsNullOrEmpty(capturedOutput))
                        {
                            OutputCallback?.Invoke(capturedOutput);
                        }

                        // Get all variables from the scope
                        var namesObj = scope.Dir();
                        for (int i = 0; i < namesObj.Length(); i++)
                        {
                            var name = namesObj[i].As<string>();

                            // Skip built-in variables and our helper functions
                            if (!name.StartsWith("__") && !name.StartsWith("_captured_") &&
                                !name.Equals("sys") && !name.Equals("StringIO"))
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
                                    // If conversion fails, store as string representation
                                    var pyValue = scope.Get(name);
                                    variables[name] = pyValue?.ToString() ?? "";
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
            }

            return variables;
        }

        private IDictionary<string, object> ExecutePythonCodeWithTimeout(string code, IDictionary<string, object> inputs)
        {
            var variables = new Dictionary<string, object>();
            outputBuffer.Clear();

            // Execution with timeout handling - for network operations or when direct execution fails
            using (Py.GIL())
            {
                using (var scope = Py.CreateScope())
                {
                    try
                    {
                        // Set up basic output capture
                        SetupOutputCapture(scope);

                        // Set up network configuration for operations that may involve I/O
                        SetupNetworkConfiguration(scope);

                        // Set input variables in Python scope
                        foreach (var kvp in inputs)
                        {
                            using (var pyValue = ConvertToPython(kvp.Value))
                            {
                                scope.Set(kvp.Key, pyValue);
                            }
                        }

                        // Execute code
                        scope.Exec(code);

                        // Get output from captured streams
                        var capturedOutput = GetCapturedOutput(scope);
                        if (!string.IsNullOrEmpty(capturedOutput))
                        {
                            OutputCallback?.Invoke(capturedOutput);
                        }

                        // Get all variables from the scope
                        var namesObj = scope.Dir();
                        for (int i = 0; i < namesObj.Length(); i++)
                        {
                            var name = namesObj[i].As<string>();

                            // Skip built-in variables and our helper functions
                            if (!name.StartsWith("__") && !name.StartsWith("_captured_") &&
                                !name.Equals("sys") && !name.Equals("StringIO") &&
                                !name.StartsWith("safe_") && !name.Equals("_original_get") &&
                                !name.Equals("get_with_timeout") && !name.Equals("_default_timeout"))
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
                                    // If conversion fails, store as string representation
                                    var pyValue = scope.Get(name);
                                    variables[name] = pyValue?.ToString() ?? "";
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
            }

            return variables;
        }

        private void SetupNetworkConfiguration(PyModule scope)
        {
            try
            {
                var networkTimeout = Math.Min(configuration.NetworkTimeoutSeconds, 10); // Cap at 10 seconds for web apps
                
                // Very simple network configuration focused on timeouts and web app optimization
                var networkConfigScript = $@"
import socket

# Set aggressive timeout for all socket operations to prevent hanging in web apps
socket.setdefaulttimeout({networkTimeout}.0)

# Basic requests configuration if available
try:
    import requests
    
    # Create a simple session with timeout
    _default_timeout = {networkTimeout}
    
    # Override requests.get with timeout
    _original_get = requests.get
    def get_with_timeout(*args, **kwargs):
        kwargs.setdefault('timeout', _default_timeout)
        kwargs.setdefault('stream', False)  # Don't stream to avoid hanging
        return _original_get(*args, **kwargs)
    
    requests.get = get_with_timeout
    
except ImportError:
    pass  # requests not available
except Exception:
    pass  # ignore any configuration errors
";
                
                scope.Exec(networkConfigScript);
            }
            catch (Exception ex)
            {
                if (configuration.EnableDebugMode)
                {
                    Console.WriteLine($"Warning: Failed to configure network settings: {ex.Message}");
                }
                // Don't throw - this is optional
            }
        }

        private void SetupOutputCapture(PyModule scope)
        {
            // Set up output capture using StringIO
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
                // Restore original stdout/stderr and get captured output
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
                using (var key = new PyString(kvp.Key))
                using (var value = ConvertToPython(kvp.Value))
                {
                    pyDict.SetItem(key, value);
                }
            }
            return pyDict;
        }

        private PyObject ConvertListToPython(IEnumerable<object> list)
        {
            var pyList = new PyList();
            foreach (var item in list)
            {
                using (var pyItem = ConvertToPython(item))
                {
                    pyList.Append(pyItem);
                }
            }
            return pyList;
        }

        private object ConvertFromPython(PyObject pyObj)
        {
            if (pyObj.IsNone())
                return null!;

            // Handle boolean values first (before checking for int types)
            // Python booleans are subclasses of int, so we need to check for bool first
            try
            {
                if (pyObj.GetPythonType().Name == "bool")
                {
                    return pyObj.As<bool>();
                }
            }
            catch { }

            // Try to convert to common .NET types
            if (PyString.IsStringType(pyObj))
                return pyObj.As<string>();
            
            // Try numeric types
            if (PyInt.IsIntType(pyObj))
            {
                try { return pyObj.As<long>(); } catch { }
                try { return pyObj.As<int>(); } catch { }
            }
            
            if (PyFloat.IsFloatType(pyObj))
            {
                try { return pyObj.As<double>(); } catch { }
            }

            // Handle collections
            if (pyObj.IsIterable() && !PyString.IsStringType(pyObj))
            {
                // Handle dictionaries
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
                    catch
                    {
                        // Fall through to string conversion
                    }
                }
                
                // Handle lists/tuples
                try
                {
                    var list = new List<object>();
                    for (int i = 0; i < pyObj.Length(); i++)
                    {
                        list.Add(ConvertFromPython(pyObj[i]));
                    }
                    return list;
                }
                catch
                {
                    // Fall through to string conversion
                }
            }

            // Fallback to string representation
            return pyObj.ToString() ?? string.Empty;
        }

        public void Dispose()
        {
            // Note: We don't shutdown PythonEngine here because it's a global resource
            // and other instances might be using it. In a real application, you might
            // want to implement reference counting or use a different approach.
            OutputCallback = null;
            outputBuffer.Clear();
        }

        private class PythonInfo
        {
            public string Executable { get; set; } = "";
            public string? ModulePaths { get; set; }
            public string? DllPath { get; set; }
        }
    }
}