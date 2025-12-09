namespace Automaton.Studio.Services.Mcp.SystemPrompts.Languages;

/// <summary>
/// Batch/CMD-specific prompt provider for code generation.
/// Batch scripts output via SET commands which are captured as strings.
/// </summary>
public class BatchPromptProvider : ILanguagePromptProvider
{
    public string Language => "Batch";

    public string GetCodeTemplate()
    {
        return """
            @echo off
            setlocal enabledelayedexpansion

            REM Initialize output variables with defaults
            set "success=false"
            set "error_message="
            set "result="

            REM Your logic here
            REM Input variables are available as environment variables with input_ prefix
            REM Example: %input_api_key%, %input_filename%

            REM On success, set outputs
            set "success=true"
            echo INFO: Operation completed successfully

            REM Output variables for the runner to capture
            echo ::OUTPUT::success=%success%
            echo ::OUTPUT::error_message=%error_message%
            echo ::OUTPUT::result=%result%

            endlocal
            """;
    }

    public string GetOutputVariableGuidance()
    {
        return """
            ## BATCH INPUT/OUTPUT VARIABLE RULES:

            ### INPUT VARIABLES:
            
            Input variables defined in the Step Editor are injected as environment variables 
            with `input_` prefix. For example, an input variable named `api_key` becomes `%input_api_key%`.
            
            **IMPORTANT: ALWAYS use input variables FIRST. Only fall back to other environment 
            variables if explicitly requested or as a documented fallback.**

            ```batch
            REM CORRECT: Use input variable directly
            if not "%input_api_key%"=="" (
                set "api_key=%input_api_key%"
            ) else (
                set "error_message=api_key input is required"
                goto :error
            )
            ```

            ### OUTPUT FORMAT:
            Batch scripts output variables using a special format that the runner captures:
            ```batch
            echo ::OUTPUT::variable_name=value
            ```

            ### ? CORRECT - Output with proper format:
            ```batch
            REM Boolean (use lowercase true/false)
            set "success=true"
            echo ::OUTPUT::success=%success%

            REM Numbers (just the value)
            set "count=42"
            echo ::OUTPUT::count=%count%

            REM Strings
            set "message=Operation completed"
            echo ::OUTPUT::message=%message%

            REM JSON data (single line, no extra escaping)
            set "data={\"cpu\": 45, \"memory\": 67}"
            echo ::OUTPUT::data=%data%
            ```

            ### Key Principles:
            1. **Input variables have priority**: Always use `%input_*%` variables first
            2. **Don't default to other environment variables** unless explicitly requested
            3. **Use `::OUTPUT::` prefix** for the runner to capture output variables
            4. **Booleans**: Use lowercase `true` or `false`
            5. **Numbers**: Output as plain numbers
            6. **JSON**: Keep on single line with proper escaping for batch
            7. **Always initialize** variables with defaults at the start
            8. **Error handling**: Use `if errorlevel` or check for failures
            """;
    }

    public string GetSystemMetricsExample()
    {
        return """
            ## BATCH EXAMPLE - System Information:

            ```batch
            @echo off
            setlocal enabledelayedexpansion

            REM Initialize outputs
            set "success=false"
            set "error_message="
            set "hostname="
            set "os_name="
            set "cpu_cores=0"

            REM Get system information
            for /f "tokens=*" %%a in ('hostname') do set "hostname=%%a"
            
            for /f "tokens=2 delims==" %%a in ('wmic os get caption /value ^| find "="') do (
                set "os_name=%%a"
            )

            set "cpu_cores=%NUMBER_OF_PROCESSORS%"

            REM Check if we got data
            if not "!hostname!"=="" (
                set "success=true"
                echo INFO: Retrieved system info for !hostname!
            ) else (
                set "error_message=Failed to retrieve hostname"
                echo ERROR: !error_message!
            )

            REM Output variables
            echo ::OUTPUT::success=%success%
            echo ::OUTPUT::error_message=%error_message%
            echo ::OUTPUT::hostname=%hostname%
            echo ::OUTPUT::os_name=%os_name%
            echo ::OUTPUT::cpu_cores=%cpu_cores%

            endlocal
            ```

            This outputs:
            - `success`: true (string, parsed as boolean by runner)
            - `hostname`: "MYPC" (string)
            - `cpu_cores`: "8" (string, parse with parseInt on frontend)

            Note: Batch has limited type support. Complex data is better handled with Python.
            """;
    }
}
