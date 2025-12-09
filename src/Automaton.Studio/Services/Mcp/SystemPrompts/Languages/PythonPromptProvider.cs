namespace Automaton.Studio.Services.Mcp.SystemPrompts.Languages;

/// <summary>
/// Python-specific prompt provider for code generation.
/// Ensures Python code outputs properly formatted data that doesn't require
/// extra parsing on the frontend.
/// </summary>
public class PythonPromptProvider : ILanguagePromptProvider
{
    public string Language => "Python";

    public string GetCodeTemplate()
    {
        return """
            ```python
            import json

            # Standard output variables - ALWAYS include these
            success = False
            error_message = ""

            # Data outputs - define all expected outputs with defaults
            # For simple values, output them directly (not as JSON strings)
            cpu_percent = 0.0
            memory_percent = 0.0
            disk_percent = 0.0

            # For complex data, use a dict that will be auto-serialized
            # Do NOT call json.dumps() - the runner handles serialization
            data = {}

            try:
                # Your logic here
                # NOTE: Input variables arrive as STRINGS - convert as needed:
                # my_number = int(count) or float(value)
                # my_bool = enabled.lower() == 'true'
                # my_dict = json.loads(config)
                
                success = True
                print("INFO: Operation completed successfully")
            except Exception as e:
                error_message = str(e)
                print(f"ERROR: {error_message}")
            ```
            """;
    }

    public string GetOutputVariableGuidance()
    {
        return """
            ## PYTHON INPUT/OUTPUT VARIABLE RULES:

            ### INPUT VARIABLES - Always arrive as STRINGS:
            
            Input variables defined in the Step Editor are injected as global variables using their **exact name** (no prefix).
            For example, an input variable named `api_key` becomes `api_key`.
            
            **IMPORTANT: ALWAYS use input variables FIRST. Only fall back to environment 
            variables if explicitly requested or as a documented fallback.**

            ```python
            # CORRECT: Check input variable first, use it if provided
            if 'api_key' in globals() and api_key:
                key = api_key.strip()
            else:
                raise ValueError("api_key input is required")
            ```

            ```python
            # Input variables are injected as global strings
            # Convert them to the types you need:
            
            # String to int
            count_val = int(count)  # count = "5" -> 5
            
            # String to float
            threshold_val = float(threshold)  # threshold = "0.75" -> 0.75
            
            # String to boolean
            include_details = enabled.lower() == 'true'  # "true" -> True
            
            # String (JSON) to dict/list
            config_dict = json.loads(config)  # '{"key": "value"}' -> {"key": "value"}
            ```

            ### HTTP REQUESTS - Use `requests` library (NOT urllib):
            
            **IMPORTANT: Always use the `requests` library for HTTP calls.**
            Do NOT use `urllib.request` - it can cause connection issues with the runner.

            ```python
            # ? CORRECT - Use requests library
            import requests
            
            response = requests.get(url, params=params, timeout=15)
            response.raise_for_status()
            data = response.json()
            ```

            ```python
            # ? WRONG - Do NOT use urllib.request (causes connection issues)
            # import urllib.request
            # with urllib.request.urlopen(url, timeout=15) as resp:  # DON'T DO THIS!
            #     data = json.loads(resp.read())
            ```

            ### OUTPUT VARIABLES - Use native Python types:

            #### ? CORRECT - Output native Python types:
            ```python
            # Booleans - use Python True/False (not strings)
            success = True

            # Numbers - use native int/float
            cpu_percent = 45.2
            count = 10

            # Strings - use plain strings
            error_message = ""
            status = "completed"

            # Complex data - use dict/list directly (NOT json.dumps!)
            data = {
                "cpu": 45.2,
                "memory": 67.8,
                "items": ["a", "b", "c"]
            }
            ```

            #### ? WRONG - Don't stringify outputs:
            ```python
            # DON'T do this - causes double-escaping!
            success = "True"           # Wrong! Use: success = True
            cpu_percent = "45.2"       # Wrong! Use: cpu_percent = 45.2
            data = json.dumps({...})   # Wrong! Use: data = {...}
            metrics_json = json.dumps(metrics)  # Wrong! Just use: metrics = {...}
            ```

            ### Key Principles:
            1. **Input variables use exact names**: `api_key` not `input_api_key`
            2. **Inputs are strings**: Convert with `int()`, `float()`, `json.loads()`, etc.
            3. **Don't default to environment variables** unless explicitly requested
            4. **Use `requests` library** for HTTP calls (NOT urllib.request)
            5. **Outputs are native types**: Booleans, numbers, dicts, lists - NOT stringified
            6. **Never call `json.dumps()`** on outputs - the runner handles serialization
            """;
    }

    public string GetSystemMetricsExample()
    {
        return """
            ## PYTHON EXAMPLE - System Metrics:

            ```python
            import psutil

            # Initialize outputs with defaults
            success = False
            error_message = ""
            cpu_percent = 0.0
            memory_percent = 0.0
            disk_percent = 0.0
            memory_total_gb = 0.0
            disk_total_gb = 0.0

            # For complex data, use a dict (NOT json.dumps!)
            metrics = {}

            try:
                cpu = psutil.cpu_percent(interval=1)
                memory = psutil.virtual_memory()
                disk = psutil.disk_usage('/')
                
                # Set individual outputs as native types
                cpu_percent = cpu
                memory_percent = memory.percent
                disk_percent = disk.percent
                memory_total_gb = round(memory.total / (1024**3), 2)
                disk_total_gb = round(disk.total / (1024**3), 2)
                
                # Complex data as dict (runner auto-serializes to JSON)
                metrics = {
                    "cpu": cpu,
                    "memory": memory.percent,
                    "disk": disk.percent,
                    "memory_total_gb": round(memory.total / (1024**3), 2),
                    "disk_total_gb": round(disk.total / (1024**3), 2)
                }
                
                success = True
                print(f"INFO: CPU: {cpu}%, Memory: {memory.percent}%, Disk: {disk.percent}%")
            except Exception as e:
                error_message = str(e)
                print(f"ERROR: {error_message}")
            ```

            ## PYTHON EXAMPLE - HTTP API Call:

            ```python
            import requests

            # Initialize outputs
            success = False
            error_message = ""
            data = {}
            status_code = 0

            try:
                # Use requests library (NOT urllib.request!)
                response = requests.get(
                    "https://api.example.com/data",
                    params={"key": api_key},
                    timeout=15
                )
                response.raise_for_status()
                
                data = response.json()
                status_code = response.status_code
                success = True
                print(f"INFO: API call successful, status {status_code}")
            except requests.exceptions.RequestException as e:
                error_message = f"Request failed: {e}"
                print(f"ERROR: {error_message}")
            except Exception as e:
                error_message = str(e)
                print(f"ERROR: {error_message}")
            ```

            This outputs:
            - `success`: true (boolean)
            - `cpu_percent`: 45.2 (number)
            - `metrics`: {"cpu": 45.2, "memory": 67.8, ...} (object)

            Frontend can use directly:
            ```javascript
            const outputs = result.outputVariables;
            if (outputs.success) {  // Native boolean
                const cpu = outputs.cpu_percent;  // Native number
                const data = outputs.metrics;     // Native object
            }
            ```
            """;
    }
}