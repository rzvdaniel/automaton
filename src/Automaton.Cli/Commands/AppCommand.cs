using Automaton.Cli.Models;
using Automaton.Cli.Services;
using Spectre.Console;
using System.CommandLine;
using System.Text.Json;

namespace Automaton.Cli.Commands;

public static class AppCommand
{
    private static readonly JsonSerializerOptions PrettyJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static Command Build(ConfigService config)
    {
        var projectService = new LocalAppProjectService(config);
        var pythonRuntimeService = new PythonRuntimeService(config);
        var executionService = new LocalAppExecutionService(pythonRuntimeService);
        var validationService = new LocalAppValidationService(projectService, executionService, pythonRuntimeService);
        var devServer = new LocalAppDevServer(executionService);

        var command = new Command("app", "Run local app validation, controller invocation, and native dev hosting");
        command.AddCommand(BuildValidateCommand(validationService));
        command.AddCommand(BuildInvokeCommand(projectService, executionService));
        command.AddCommand(BuildDevCommand(validationService, projectService, devServer));
        return command;
    }

    private static Command BuildValidateCommand(LocalAppValidationService validationService)
    {
        var jsonOption = new Option<bool>("--json", "Print the result as JSON");
        var strictOption = new Option<bool>("--strict", "Treat warnings as failures");
        var containerOption = new Option<bool>("--container", "Validate container prerequisites instead of native Python execution");

        var command = new Command("validate", "Validate a local app without starting the preview server")
        {
            jsonOption,
            strictOption,
            containerOption
        };

        command.SetHandler(async (bool asJson, bool strict, bool containerMode) =>
        {
            try
            {
                var result = await validationService.ValidateAsync(containerMode: containerMode);
                var success = result.Success && (!strict || result.Checks.All(check => check.Status != LocalAppValidationStatuses.Warning));

                if (asJson)
                {
                    CliJson.Write(new
                    {
                        success,
                        app = new
                        {
                            root = result.Context.RootPath,
                            frontend = result.Context.FrontendPath,
                            backend = result.Context.BackendPath,
                            data = result.Context.DataPath
                        },
                        checks = result.Checks.Select(check => new { name = check.Name, status = check.Status, message = check.Message })
                    });
                }
                else
                {
                    PrintValidationTable(result.Checks);
                }

                if (!success)
                {
                    Environment.Exit(1);
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
                Environment.Exit(1);
            }
        }, jsonOption, strictOption, containerOption);

        return command;
    }

    private static Command BuildInvokeCommand(LocalAppProjectService projectService, LocalAppExecutionService executionService)
    {
        var controllerArgument = new Argument<string>("controller", "Controller file name without .py");
        var actionArgument = new Argument<string>("action", "Action name passed to handle(action_name, inputs)");
        var inputOption = new Option<string>("--input", () => "{}", "JSON input payload");
        var jsonOption = new Option<bool>("--json", "Print machine-readable output");
        var timeoutOption = new Option<int>("--timeout", () => 30, "Execution timeout in seconds");
        var mockOnlyOption = new Option<bool>("--mock-only", "Force mock resolution only");
        var mockFallbackOption = new Option<bool>("--mock-fallback", "Use mock files when the controller does not exist");
        var verboseOption = new Option<bool>("--verbose", "Print stdout, stderr, and traceback details");

        var command = new Command("invoke", "Run a local controller action without starting the preview server")
        {
            controllerArgument,
            actionArgument,
            inputOption,
            jsonOption,
            timeoutOption,
            mockOnlyOption,
            mockFallbackOption,
            verboseOption
        };

        command.SetHandler(async (string controller, string action, string input, bool asJson, int timeoutSeconds, bool mockOnly, bool mockFallback, bool verbose) =>
        {
            try
            {
                var context = projectService.ResolveContext();
                var mode = ResolveMode(mockOnly, mockFallback, LocalAppExecutionModes.Native);
                var result = await executionService.ExecuteAsync(context, new LocalAppExecutionOptions
                {
                    ControllerName = controller,
                    ActionName = action,
                    InputsJson = input,
                    Mode = mode,
                    Timeout = TimeSpan.FromSeconds(timeoutSeconds),
                    Verbose = verbose
                });

                if (asJson)
                {
                    CliJson.Write(new
                    {
                        success = result.Success,
                        mode = result.Mode,
                        controller = result.ControllerName,
                        action = result.ActionName,
                        durationMs = result.Duration.TotalMilliseconds,
                        data = result.Data,
                        error = result.Error,
                        failureReason = result.FailureReason,
                        traceback = verbose ? result.Traceback : null,
                        stdout = verbose ? result.StdOut : null,
                        stderr = verbose ? result.StdErr : null
                    });
                }
                else
                {
                    PrintInvocationResult(result, verbose);
                }

                if (!result.Success)
                {
                    Environment.Exit(1);
                }
            }
            catch (JsonException ex)
            {
                AnsiConsole.MarkupLine($"[red]Invalid JSON input:[/] {Markup.Escape(ex.Message)}");
                Environment.Exit(1);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
                Environment.Exit(1);
            }
        }, controllerArgument, actionArgument, inputOption, jsonOption, timeoutOption, mockOnlyOption, mockFallbackOption, verboseOption);

        return command;
    }

    private static Command BuildDevCommand(LocalAppValidationService validationService, LocalAppProjectService projectService, LocalAppDevServer devServer)
    {
        var portOption = new Option<int?>("--port", "Preferred local port");
        var hostOption = new Option<string>("--host", () => "localhost", "Host name to bind");
        var openOption = new Option<bool>("--open", "Open the preview URL in a browser");
        var noBrowserOption = new Option<bool>("--no-browser", "Do not try to open a browser");
        var mockOnlyOption = new Option<bool>("--mock-only", "Force controller calls to resolve through backend/mocks");
        var mockFallbackOption = new Option<bool>("--mock-fallback", "Use mock files only when the controller is missing");
        var verboseOption = new Option<bool>("--verbose", "Print controller stdout, stderr, and file watch events");
        var containerOption = new Option<bool>("--container", "Reserved for future container parity mode");

        var command = new Command("dev", "Start the native local app runtime and preview server")
        {
            portOption,
            hostOption,
            openOption,
            noBrowserOption,
            mockOnlyOption,
            mockFallbackOption,
            verboseOption,
            containerOption
        };

        command.SetHandler(async (int? port, string host, bool openBrowser, bool noBrowser, bool mockOnly, bool mockFallback, bool verbose, bool containerMode) =>
        {
            try
            {
                if (containerMode)
                {
                    throw new NotSupportedException("Container parity mode is not implemented yet. Use native mode for now.");
                }

                var validation = await validationService.ValidateAsync();
                PrintValidationTable(validation.Checks);
                if (!validation.Success)
                {
                    Environment.Exit(1);
                    return;
                }

                var mode = ResolveMode(mockOnly, mockFallback, LocalAppExecutionModes.MockFallback);
                var context = projectService.ResolveContext();
                await devServer.RunAsync(context, new LocalAppDevServerOptions
                {
                    Host = host,
                    Port = port,
                    Mode = mode,
                    Verbose = verbose,
                    OpenBrowser = openBrowser && !noBrowser
                });
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
                Environment.Exit(1);
            }
        }, portOption, hostOption, openOption, noBrowserOption, mockOnlyOption, mockFallbackOption, verboseOption, containerOption);

        return command;
    }

    private static string ResolveMode(bool mockOnly, bool mockFallback, string defaultMode)
    {
        if (mockOnly && mockFallback)
        {
            throw new InvalidOperationException("Choose either --mock-only or --mock-fallback, not both.");
        }

        if (mockOnly)
        {
            return LocalAppExecutionModes.MockOnly;
        }

        if (mockFallback)
        {
            return LocalAppExecutionModes.MockFallback;
        }

        return defaultMode;
    }

    private static void PrintValidationTable(IEnumerable<LocalAppValidationCheck> checks)
    {
        var table = new Table();
        table.AddColumn("Check");
        table.AddColumn("Status");
        table.AddColumn("Message");

        foreach (var check in checks)
        {
            var status = check.Status switch
            {
                LocalAppValidationStatuses.Pass => "[green]pass[/]",
                LocalAppValidationStatuses.Warning => "[yellow]warn[/]",
                _ => "[red]fail[/]"
            };

            table.AddRow(check.Name, status, Markup.Escape(check.Message));
        }

        AnsiConsole.Write(table);
    }

    private static void PrintInvocationResult(LocalAppExecutionResult result, bool verbose)
    {
        var status = result.Success ? "[green]success[/]" : "[red]failure[/]";
        AnsiConsole.MarkupLine($"[bold]{Markup.Escape(result.ControllerName)}.{Markup.Escape(result.ActionName)}[/] {status} [grey]({result.Mode}, {result.Duration.TotalMilliseconds:F0} ms)[/]");

        if (result.Success)
        {
            var payload = result.Data is null
                ? "null"
                : JsonSerializer.Serialize(result.Data, PrettyJsonOptions);
            AnsiConsole.WriteLine(payload);
        }
        else
        {
            AnsiConsole.MarkupLine($"[red]Reason:[/] {Markup.Escape(result.Error)}");
            if (verbose && !string.IsNullOrWhiteSpace(result.Traceback))
            {
                AnsiConsole.WriteLine(result.Traceback);
            }
        }

        if (verbose)
        {
            if (!string.IsNullOrWhiteSpace(result.StdOut))
            {
                AnsiConsole.MarkupLine("[grey]stdout:[/]");
                AnsiConsole.WriteLine(result.StdOut);
            }

            if (!string.IsNullOrWhiteSpace(result.StdErr))
            {
                AnsiConsole.MarkupLine("[grey]stderr:[/]");
                AnsiConsole.WriteLine(result.StdErr);
            }
        }
    }
}
