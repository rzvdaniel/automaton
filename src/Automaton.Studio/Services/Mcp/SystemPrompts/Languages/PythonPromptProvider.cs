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
            ## PYTHON OUTPUT VARIABLE RULES:

            ### ? CORRECT - Output native Python types:
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

            ### ? WRONG - Don't stringify values:
            ```python
            # DON'T do this - causes double-escaping!
            success = "True"           # Wrong! Use: success = True
            cpu_percent = "45.2"       # Wrong! Use: cpu_percent = 45.2
            data = json.dumps({...})   # Wrong! Use: data = {...}
            metrics_json = json.dumps(metrics)  # Wrong! Just use: metrics = {...}
            ```

            ### Key Principles:
            1. **Booleans**: Use Python `True`/`False`, not strings `"true"`/`"false"`
            2. **Numbers**: Use native `int` or `float`, not strings
            3. **Complex data**: Use `dict` or `list` directly - the runner serializes them
            4. **Never call `json.dumps()`** on output variables - this causes double-escaping
            5. **String outputs**: Only use strings for actual text data like `error_message`
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

            This outputs:
            - `success`: true (boolean)
            - `cpu_percent`: 45.2 (number)
            - `metrics`: {"cpu": 45.2, "memory": 67.8, ...} (object)

            Frontend can use directly:
            ```javascript
            const outputs = response.data.outputVariables;
            if (outputs.success) {  // Native boolean
                const cpu = outputs.cpu_percent;  // Native number
                const data = outputs.metrics;     // Native object
            }
            ```
            """;
    }
}
