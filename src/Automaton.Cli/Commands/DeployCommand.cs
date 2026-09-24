using System.Diagnostics;
using Automaton.Cli.Models;
using Automaton.Cli.Services;
using Spectre.Console;
using System.CommandLine;
using System.CommandLine.Invocation;

namespace Automaton.Cli.Commands;

/// <summary>
/// automaton deploy [--clean] [--publish]
///
/// Workflow:
///   1. Read automaton.json for appId + application contract
///   2. Validate the contract and package source plus the runtime artifact when applicable
///   3. Upload the package; the assigned Runner executes the configured build once
///   4. Deploy Actions and the configured runtime
/// </summary>
public static class DeployCommand
{
    public static Command Build(ConfigService config)
    {
        var targetArgument = new Argument<string?>(
            "target",
            () => null,
            "Remote deployment target slug; omit to deploy the only target or choose interactively")
        {
            Arity = ArgumentArity.ZeroOrOne
        };
        var runnerOption = new Option<string?>("--runner", "Runner ID or exact runner name to assign before deployment when needed");
        var cleanOption = new Option<bool>(
            "--clean",
            "Wipe all server-side app files before uploading (full replace)");

        var dirOption = new Option<string?>(
            ["--dir", "-d"],
            "Application artifact directory to upload (overrides application.build.outputDirectory in automaton.json)");

        var jsonOption = new Option<bool>("--json", "Print the result as JSON");
        var verboseOption = new Option<bool>("--verbose", "Print ignored-file details and exclusion reasons");
        var publishOption = new Option<bool>("--publish", "Publish the app after a successful deploy");
        var openOption = new Option<bool>("--open", "Open the deployed app in the browser after success");
        var nonInteractiveOption = new Option<bool>("--non-interactive", "Disable prompts and rich terminal rendering");
        var environmentOption = new Option<string?>("--env", "Use a local project linkage/credential profile from ./.automaton/environments/<name>.json");

        var cmd = new Command("deploy", "Build, upload, and deploy the app to Automaton")
        {
            targetArgument,
            runnerOption,
            cleanOption,
            dirOption,
            jsonOption,
            verboseOption,
            publishOption,
            openOption,
            nonInteractiveOption,
            environmentOption
        };

        cmd.SetHandler(context => ExecuteAsync(context, config, targetArgument, runnerOption, cleanOption, dirOption, jsonOption, verboseOption, publishOption, openOption, nonInteractiveOption, environmentOption));

        return cmd;
    }


    private static async Task ExecuteAsync(
        InvocationContext context,
        ConfigService config,
        Argument<string?> targetArgument,
        Option<string?> runnerOption,
        Option<bool> cleanOption,
        Option<string?> dirOption,
        Option<bool> jsonOption,
        Option<bool> verboseOption,
        Option<bool> publishOption,
        Option<bool> openOption,
        Option<bool> nonInteractiveOption,
        Option<string?> environmentOption)
    {
        var options = ReadOptions(context, targetArgument, runnerOption, cleanOption, dirOption,
            jsonOption, verboseOption, publishOption, openOption, nonInteractiveOption, environmentOption);
        var publishService = new PublishService(config);
        var deployService = CreateDeployService(config, options);
        try
        {
            var setup = await PrepareDeploymentAsync(config, context, options, deployService);
            if (setup.Failure is not null)
            {
                DeployCommandOutput.WriteResult(setup.Failure, options.AsJson, options.Verbose, options.NonInteractive);
                context.ExitCode = 1;
                return;
            }

            var result = await DeployToTargetAsync(deployService, setup, options, context.GetCancellationToken());
            result = await CompleteDeploymentAsync(result, setup, config, options, context.GetCancellationToken());
            await PresentDeploymentResultAsync(result, publishService, setup, config, options, context);
        }
        catch (Exception exception)
        {
            WriteFailure(exception, options, context);
        }
    }

    private static DeployCommandOptions ReadOptions(
        InvocationContext context,
        Argument<string?> targetArgument,
        Option<string?> runnerOption,
        Option<bool> cleanOption,
        Option<string?> dirOption,
        Option<bool> jsonOption,
        Option<bool> verboseOption,
        Option<bool> publishOption,
        Option<bool> openOption,
        Option<bool> nonInteractiveOption,
        Option<string?> environmentOption)
    {
        var asJson = context.ParseResult.GetValueForOption(jsonOption);
        return new DeployCommandOptions(
            context.ParseResult.GetValueForArgument(targetArgument),
            context.ParseResult.GetValueForOption(runnerOption),
            context.ParseResult.GetValueForOption(cleanOption),
            context.ParseResult.GetValueForOption(dirOption),
            asJson,
            context.ParseResult.GetValueForOption(verboseOption),
            context.ParseResult.GetValueForOption(publishOption),
            context.ParseResult.GetValueForOption(openOption),
            CliInteraction.IsNonInteractive(context.ParseResult.GetValueForOption(nonInteractiveOption), asJson),
            context.ParseResult.GetValueForOption(environmentOption));
    }

    private static DeployService CreateDeployService(ConfigService config, DeployCommandOptions options) =>
        new(config,
            message => DeployCommandOutput.WriteProgress(message, options.AsJson, options.NonInteractive),
            check => DeployCommandOutput.WriteCheck(check, options.AsJson, options.NonInteractive));

    private static async Task<DeploymentSetup> PrepareDeploymentAsync(
        ConfigService config,
        InvocationContext context,
        DeployCommandOptions options,
        DeployService deployService)
    {
        var cwd = Directory.GetCurrentDirectory();
        var localFailure = ValidateRunnerProject(deployService, cwd, options);
        if (localFailure is not null) return DeploymentSetup.Failed(cwd, localFailure);

        var projectConfig = config.TryLoadProjectConfig(cwd);
        var resolvedContext = config.ResolveContext(projectConfig, cwd, options.EnvironmentName);
        var targetService = new DeploymentTargetService(config);
        var target = await ResolveTargetAsync(targetService, context, cwd, options);
        ValidateTarget(target, options);
        var headingWritten = false;
        DeployCommandOutput.WriteDeployHeading(projectConfig?.Name, resolvedContext.AppId, resolvedContext.EnvironmentName,
            target, resolvedContext.Server, options.AsJson, options.NonInteractive, ref headingWritten);
        return new DeploymentSetup(cwd, projectConfig, resolvedContext, targetService, target, CreatePreparation(target), null);
    }

    private static DeployResult? ValidateRunnerProject(DeployService deployService, string cwd, DeployCommandOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Runner)) return null;
        return deployService.ValidateLocalProject(cwd, options.Clean, options.Directory, options.EnvironmentName);
    }

    private static async Task<DeploymentTargetInfo> ResolveTargetAsync(
        DeploymentTargetService targetService,
        InvocationContext context,
        string cwd,
        DeployCommandOptions options)
    {
        var allTargets = (await targetService.ListAsync(cwd, options.EnvironmentName, context.GetCancellationToken())).Targets;
        var target = string.IsNullOrWhiteSpace(options.Runner)
            ? DeploymentTargetService.SelectForDeploy(allTargets, options.TargetSlug, options.NonInteractive)
            : DeploymentTargetService.SelectEnvironment(allTargets, options.TargetSlug, options.NonInteractive);
        if (string.IsNullOrWhiteSpace(options.Runner)) return target;
        if (target.Status == "Deploying")
            throw new InvalidOperationException($"Environment '{target.Slug}' is already Deploying and cannot be reassigned.");
        return await targetService.AssignRunnerAsync(cwd, target, options.Runner, options.EnvironmentName, context.GetCancellationToken());
    }

    private static void ValidateTarget(DeploymentTargetInfo target, DeployCommandOptions options)
    {
        DeploymentTargetService.EnsureDeployable(target);
        if (!target.IsDefault && target.Runner is null)
            throw new InvalidOperationException($"Environment '{target.Slug}' has no assigned Runner. Assign one with 'automaton deploy {target.Slug} --runner <runner-id-or-name>'.");
        if (!target.IsDefault && (options.Publish || options.Open))
            throw new InvalidOperationException("--publish and --open apply only to the Live target.");
    }

    private static DeployPreparationResult CreatePreparation(DeploymentTargetInfo target) => new()
    {
        Success = true,
        RunnerId = target.Runner?.Id,
        RunnerName = target.Runner?.Name,
        Checks = target.Runner is null
            ? []
            : [new DeployCheckResult(DeployCheckNames.RunnerAssignment, true,
                $"environment '{target.Slug}' assigned to Runner {target.Runner.Name}")]
    };

    private static async Task<DeployResult> DeployToTargetAsync(
        DeployService deployService,
        DeploymentSetup setup,
        DeployCommandOptions options,
        CancellationToken cancellationToken) =>
        await deployService.DeployAsync(setup.Directory, options.Clean, options.Directory,
            options.EnvironmentName, setup.Target, cancellationToken);

    private static async Task<DeployResult> CompleteDeploymentAsync(
        DeployResult result,
        DeploymentSetup setup,
        ConfigService config,
        DeployCommandOptions options,
        CancellationToken cancellationToken)
    {
        if (!result.Success) return MergePreparation(result, setup.Preparation);
        var target = await setup.TargetService.ResolveAsync(setup.Directory, setup.Target.Slug,
            options.EnvironmentName, cancellationToken);
        return MergePreparation(ApplyTargetIdentity(result, target), setup.Preparation);
    }

    private static async Task PresentDeploymentResultAsync(
        DeployResult result,
        PublishService publishService,
        DeploymentSetup setup,
        ConfigService config,
        DeployCommandOptions options,
        InvocationContext context)
    {
        if (!result.Success)
        {
            DeployCommandOutput.WriteResult(result, options.AsJson, options.Verbose, options.NonInteractive);
            context.ExitCode = 1;
            return;
        }

        if (options.Publish)
        {
            var publishResult = await publishService.PublishAsync(setup.Directory, null, null, null, yes: true,
                environmentName: options.EnvironmentName);
            WriteDeployAndPublishResults(result, publishResult, options);
            if (!publishResult.Success)
            {
                context.ExitCode = 1;
                return;
            }
            if (options.Open) CliBrowserService.OpenUrl(publishResult.Url!);
        }
        else
        {
            result = AppendDefaultSuccessNextSteps(result);
            DeployCommandOutput.WriteResult(result, options.AsJson, options.Verbose, options.NonInteractive);
            if (options.Open) await OpenRuntimeAsync(result, config, setup.Directory, options.EnvironmentName);
        }

        context.ExitCode = 0;
    }

    private static void WriteDeployAndPublishResults(DeployResult result, PublishResult publishResult, DeployCommandOptions options)
    {
        if (options.AsJson)
        {
            DeployCommandOutput.WriteCombinedJson(result, publishResult);
            return;
        }
        DeployCommandOutput.WriteResult(result, false, options.Verbose, options.NonInteractive);
        PublishCommand.WriteResult(publishResult, false, options.NonInteractive);
    }

    private static async Task OpenRuntimeAsync(DeployResult result, ConfigService config, string cwd, string? environmentName)
    {
        var (client, server) = DeployCommandOutput.CreateApiClient(config, cwd, environmentName);
        using (client)
        {
            var launchCode = await client.CreateRuntimeLaunchAsync(result.AppId!);
            CliBrowserService.OpenUrl(CliAppUrlBuilder.BuildRuntimeLaunchUrl(server, result.AppId, launchCode));
        }
    }

    private static void WriteFailure(Exception exception, DeployCommandOptions options, InvocationContext context)
    {
        if (options.AsJson)
        {
            CliJson.Write(new { success = false, summary = "Deploy failed", error = exception.Message, target = options.TargetSlug });
        }
        else if (options.NonInteractive)
        {
            Console.Error.WriteLine($"Error: {exception.Message}");
        }
        else
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(exception.Message)}");
        }
        context.ExitCode = 1;
    }

    private sealed record DeployCommandOptions(
        string? TargetSlug,
        string? Runner,
        bool Clean,
        string? Directory,
        bool AsJson,
        bool Verbose,
        bool Publish,
        bool Open,
        bool NonInteractive,
        string? EnvironmentName);

    private sealed record DeploymentSetup(
        string Directory,
        AutomatonConfig? ProjectConfig,
        ResolvedCliContext Context,
        DeploymentTargetService TargetService,
        DeploymentTargetInfo Target,
        DeployPreparationResult Preparation,
        DeployResult? Failure)
    {
        public static DeploymentSetup Failed(string directory, DeployResult failure) =>
            new(directory, null, new ResolvedCliContext(), null!, null!, null!, failure);
    }
    private static DeployResult MergePreparation(DeployResult result, DeployPreparationResult preparation)
    {
        if (preparation.Checks.Count == 0 && preparation.NextSteps.Count == 0)
        {
            return result;
        }

        var automaticRunnerReady = result.Success && result.Runtime?.Success == true && result.Runtime.RunnerCount > 0;
        var preparationChecks = automaticRunnerReady
            ? preparation.Checks.Where(check => check.Name != DeployCheckNames.RunnerLookup || check.Success).ToList()
            : preparation.Checks;
        var preparationNextSteps = automaticRunnerReady
            ? preparation.NextSteps.Where(step => !step.Contains("runner", StringComparison.OrdinalIgnoreCase)).ToList()
            : preparation.NextSteps;

        return new DeployResult
        {
            Success = result.Success,
            Summary = result.Summary,
            HasWarnings = result.HasWarnings || preparationChecks.Any(check => !check.Success && check.IsWarning),
            AppId = result.AppId,
            AppName = result.AppName,
            TargetSlug = result.TargetSlug,
            TargetName = result.TargetName,
            RunnerName = result.RunnerName,
            DeploymentMode = result.DeploymentMode,
            OpenAppUrl = result.OpenAppUrl,
            StudioAppUrl = result.StudioAppUrl,
            DeployDirectory = result.DeployDirectory,
            Clean = result.Clean,
            Checks = preparationChecks.Concat(result.Checks).ToList(),
            NextSteps = preparationNextSteps.Concat(result.NextSteps).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            SourceUploaded = result.SourceUploaded,
            RuntimeUploaded = result.RuntimeUploaded,
            Runtime = result.Runtime,
            RuntimeRunning = result.RuntimeRunning
        };
    }

    private static DeployResult ApplyTargetIdentity(
        DeployResult result,
        DeploymentTargetInfo target) =>
        new()
        {
            Success = result.Success,
            Summary = result.Summary,
            HasWarnings = result.HasWarnings,
            AppId = result.AppId,
            AppName = result.AppName,
            TargetSlug = target.Slug,
            TargetName = target.Name,
            RunnerName = target.Runner?.Name,
            DeploymentMode = target.IsDefault ? "default" : "named",
            OpenAppUrl = result.OpenAppUrl,
            StudioAppUrl = result.StudioAppUrl,
            DeployDirectory = result.DeployDirectory,
            Clean = result.Clean,
            Checks = result.Checks,
            NextSteps = result.NextSteps,
            SourceUploaded = result.SourceUploaded,
            RuntimeUploaded = result.RuntimeUploaded,
            Runtime = result.Runtime,
            RuntimeRunning = result.RuntimeRunning
        };

    private static DeployResult AppendDefaultSuccessNextSteps(DeployResult result)
    {
        if (!result.Success ||
            string.Equals(result.DeploymentMode, "named", StringComparison.Ordinal))
        {
            return result;
        }

        var nextSteps = result.NextSteps.ToList();
        AddNextStep(nextSteps, "Run automaton publish --yes --slug <your-app-slug> when you want to make this deploy public");
        AddNextStep(nextSteps, "Or use automaton deploy --publish on the next deploy to deploy and publish in one command");

        return new DeployResult
        {
            Success = result.Success,
            Summary = result.Summary,
            AppId = result.AppId,
            AppName = result.AppName,
            TargetSlug = result.TargetSlug,
            TargetName = result.TargetName,
            RunnerName = result.RunnerName,
            DeploymentMode = result.DeploymentMode,
            OpenAppUrl = result.OpenAppUrl,
            StudioAppUrl = result.StudioAppUrl,
            DeployDirectory = result.DeployDirectory,
            Clean = result.Clean,
            Checks = result.Checks,
            NextSteps = nextSteps,
            SourceUploaded = result.SourceUploaded,
            RuntimeUploaded = result.RuntimeUploaded,
            Runtime = result.Runtime,
            HasWarnings = result.HasWarnings,
            RuntimeRunning = result.RuntimeRunning
        };
    }

    private static void AddNextStep(ICollection<string> nextSteps, string value)
    {
        if (nextSteps.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        nextSteps.Add(value);
    }


}
