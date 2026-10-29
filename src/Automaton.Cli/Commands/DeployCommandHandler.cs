using Automaton.Cli.Models;
using Automaton.Cli.Services;
using Spectre.Console;
using System.CommandLine;
using System.CommandLine.Invocation;

namespace Automaton.Cli.Commands;

internal static class DeployCommandHandler
{
    internal static async Task HandleAsync(
        ConfigService config,
        InvocationContext context,
        Argument<string?> targetArgument,
        Option<string?> runnerOption,
        Option<bool> cleanOption,
        Option<string?> directoryOption,
        Option<bool> jsonOption,
        Option<bool> verboseOption,
        Option<bool> publishOption,
        Option<bool> openOption,
        Option<bool> nonInteractiveOption,
        Option<string?> environmentOption)
    {
        var arguments = ReadArguments(
            context, targetArgument, runnerOption, cleanOption, directoryOption, jsonOption,
            verboseOption, publishOption, openOption, nonInteractiveOption, environmentOption);
        var nonInteractive = CliInteraction.IsNonInteractive(arguments.NonInteractive, arguments.AsJson);
        try
        {
            var plan = await PrepareDeploymentAsync(config, context, arguments, nonInteractive);
            if (plan == null) return;
            var result = await DeployAsync(context, arguments, plan);
            var success = await CompleteDeploymentAsync(config, arguments, plan, result, nonInteractive);
            context.ExitCode = success ? 0 : 1;
        }
        catch (Exception exception)
        {
            WriteFailure(exception, arguments.TargetSlug, arguments.AsJson, nonInteractive);
            context.ExitCode = 1;
        }
    }

    private static DeployArguments ReadArguments(
        InvocationContext context,
        Argument<string?> targetArgument,
        Option<string?> runnerOption,
        Option<bool> cleanOption,
        Option<string?> directoryOption,
        Option<bool> jsonOption,
        Option<bool> verboseOption,
        Option<bool> publishOption,
        Option<bool> openOption,
        Option<bool> nonInteractiveOption,
        Option<string?> environmentOption)
        => new(
            context.ParseResult.GetValueForArgument(targetArgument),
            context.ParseResult.GetValueForOption(runnerOption),
            context.ParseResult.GetValueForOption(cleanOption),
            context.ParseResult.GetValueForOption(directoryOption),
            context.ParseResult.GetValueForOption(jsonOption),
            context.ParseResult.GetValueForOption(verboseOption),
            context.ParseResult.GetValueForOption(publishOption),
            context.ParseResult.GetValueForOption(openOption),
            context.ParseResult.GetValueForOption(nonInteractiveOption),
            context.ParseResult.GetValueForOption(environmentOption));

    private static async Task<DeploymentPlan?> PrepareDeploymentAsync(
        ConfigService config,
        InvocationContext invocation,
        DeployArguments arguments,
        bool nonInteractive)
    {
        var cwd = Directory.GetCurrentDirectory();
        var deployService = CreateDeployService(config, arguments, nonInteractive);
        if (HasInvalidLocalProject(deployService, cwd, arguments, nonInteractive))
        {
            invocation.ExitCode = 1;
            return null;
        }

        var projectConfig = config.TryLoadProjectConfig(cwd);
        var resolved = config.ResolveContext(projectConfig, cwd, arguments.EnvironmentName);
        var targetService = new DeploymentTargetService(config);
        var target = await ResolveDeploymentTargetAsync(
            targetService, cwd, arguments, nonInteractive, invocation.GetCancellationToken());
        ValidateTarget(target, arguments);
        var headingWritten = false;
        WriteHeading(projectConfig, resolved, target, arguments, nonInteractive, ref headingWritten);
        var preparation = BuildPreparation(target);
        return new DeploymentPlan(
            cwd, arguments.EnvironmentName, target, targetService, deployService, preparation);
    }

    private static DeployService CreateDeployService(
        ConfigService config, DeployArguments arguments, bool nonInteractive)
        => new(
            config,
            message => DeployCommand.WriteProgress(message, arguments.AsJson, nonInteractive),
            check => DeployCommand.WriteCheck(check, arguments.AsJson, nonInteractive));

    private static bool HasInvalidLocalProject(
        DeployService deployService, string cwd, DeployArguments arguments, bool nonInteractive)
    {
        if (string.IsNullOrWhiteSpace(arguments.Runner)) return false;
        var validation = deployService.ValidateLocalProject(
            cwd, arguments.Clean, arguments.Directory, arguments.EnvironmentName);
        if (validation == null) return false;
        DeployCommand.WriteResult(validation, arguments.AsJson, arguments.Verbose, nonInteractive);
        return true;
    }

    private static async Task<DeploymentTargetInfo> ResolveDeploymentTargetAsync(
        DeploymentTargetService targetService,
        string cwd,
        DeployArguments arguments,
        bool nonInteractive,
        CancellationToken cancellationToken)
    {
        var targets = (await targetService.ListAsync(
            cwd, arguments.EnvironmentName, cancellationToken)).Targets;
        return string.IsNullOrWhiteSpace(arguments.Runner)
            ? DeploymentTargetService.SelectForDeploy(targets, arguments.TargetSlug, nonInteractive)
            : DeploymentTargetService.SelectEnvironment(targets, arguments.TargetSlug, nonInteractive);
    }

    private static void ValidateTarget(DeploymentTargetInfo target, DeployArguments arguments)
    {
        if (!string.IsNullOrWhiteSpace(arguments.Runner) && target.Status == "Deploying")
        {
            throw new InvalidOperationException(
                $"Environment '{target.Slug}' is already Deploying and cannot be reassigned.");
        }
        if (!target.IsDefault && target.Runner == null)
        {
            throw new InvalidOperationException(
                $"Environment '{target.Slug}' has no assigned Runner. " +
                $"Assign one with 'automaton deploy {target.Slug} --runner <runner-id-or-name>'.");
        }
        if (!target.IsDefault && (arguments.Publish || arguments.Open))
        {
            throw new InvalidOperationException("--publish and --open apply only to the Live target.");
        }
    }

    private static void WriteHeading(
        AutomatonConfig? projectConfig,
        ResolvedCliContext resolved,
        DeploymentTargetInfo target,
        DeployArguments arguments,
        bool nonInteractive,
        ref bool headingWritten)
        => DeployCommand.WriteDeployHeading(
            projectConfig?.Name, resolved.AppId, resolved.EnvironmentName, target, resolved.Server,
            arguments.AsJson, nonInteractive, ref headingWritten);

    private static DeployPreparationResult BuildPreparation(
        DeploymentTargetInfo target, bool assignedAutomatically = false)
    {
        var runnerDescription = assignedAutomatically ? "the only available Runner" : "Runner";
        return new()
        {
            Success = true,
            RunnerId = target.Runner?.Id,
            RunnerName = target.Runner?.Name,
            Checks = target.Runner == null
                ? []
                : [new DeployCheckResult(
                    DeployCheckNames.RunnerAssignment,
                    true,
                    $"environment '{target.Slug}' assigned to {runnerDescription} {target.Runner.Name}")]
        };
    }

    private static async Task<DeployResult> DeployAsync(
        InvocationContext invocation,
        DeployArguments arguments,
        DeploymentPlan plan)
    {
        var selection = await EnsureRunnerAsync(invocation, arguments, plan);
        if (selection.Failure != null)
        {
            return selection.Failure;
        }

        var target = selection.Target;
        var result = await plan.DeployService.DeployAsync(
            plan.Directory, arguments.Clean, arguments.Directory,
            arguments.EnvironmentName, target, invocation.GetCancellationToken());
        if (result.Success)
        {
            target = await plan.TargetService.ResolveAsync(
                plan.Directory, target.Slug, arguments.EnvironmentName,
                invocation.GetCancellationToken());
            result = DeployCommand.ApplyTargetIdentity(result, target);
        }
        return DeployCommand.MergePreparation(result, selection.Preparation);
    }

    private sealed record RunnerSelection(
        DeploymentTargetInfo Target,
        DeployPreparationResult Preparation,
        DeployResult? Failure = null);

    private static async Task<RunnerSelection> EnsureRunnerAsync(
        InvocationContext invocation,
        DeployArguments arguments,
        DeploymentPlan plan)
    {
        var cancellationToken = invocation.GetCancellationToken();
        if (!string.IsNullOrWhiteSpace(arguments.Runner))
        {
            var assigned = await plan.TargetService.AssignRunnerAsync(
                plan.Directory, plan.Target, arguments.Runner, arguments.EnvironmentName, cancellationToken);
            return new(assigned, plan.Preparation);
        }
        if (plan.Target.Runner != null)
        {
            return new(plan.Target, plan.Preparation);
        }

        // ValidateTarget only lets the Live environment through without a Runner, as a new app has none yet.
        // Validate the project first so a broken project is not left with a new assignment.
        var invalidProject = plan.DeployService.ValidateLocalProject(
            plan.Directory, arguments.Clean, arguments.Directory, arguments.EnvironmentName);
        if (invalidProject != null)
        {
            return new(plan.Target, plan.Preparation, invalidProject);
        }
        try
        {
            var target = await plan.TargetService.AssignOnlyAvailableRunnerAsync(
                plan.Directory, plan.Target, arguments.EnvironmentName, cancellationToken);
            return new(target, BuildPreparation(target, assignedAutomatically: true));
        }
        catch (InvalidOperationException exception)
        {
            return new(plan.Target, plan.Preparation, RunnerAssignmentFailure(plan.Target, exception.Message));
        }
    }

    private static DeployResult RunnerAssignmentFailure(DeploymentTargetInfo target, string message)
        => new()
        {
            Success = false,
            Summary = "Runner assignment failed",
            TargetSlug = target.Slug,
            TargetName = target.Name,
            Checks = [new DeployCheckResult(DeployCheckNames.RunnerAssignment, false, message)],
            NextSteps =
            [
                "Run automaton runners to list available Runners",
                "Assign one with automaton set-runner <runner> or automaton deploy --runner <runner>"
            ]
        };

    private static async Task<bool> CompleteDeploymentAsync(
        ConfigService config,
        DeployArguments arguments,
        DeploymentPlan plan,
        DeployResult result,
        bool nonInteractive)
    {
        if (!result.Success)
        {
            DeployCommand.WriteResult(result, arguments.AsJson, arguments.Verbose, nonInteractive);
            return false;
        }
        if (arguments.Publish)
        {
            return await PublishDeploymentAsync(config, arguments, plan, result, nonInteractive);
        }
        await CompletePrivateDeploymentAsync(config, arguments, plan, result, nonInteractive);
        return true;
    }

    private static async Task<bool> PublishDeploymentAsync(
        ConfigService config,
        DeployArguments arguments,
        DeploymentPlan plan,
        DeployResult result,
        bool nonInteractive)
    {
        var published = await new PublishService(config).PublishAsync(
            plan.Directory, slug: null, requiresAuthentication: null, isForkable: null,
            yes: true, environmentName: plan.EnvironmentName);
        if (arguments.AsJson)
        {
            DeployCommand.WriteCombinedJson(result, published);
        }
        else
        {
            DeployCommand.WriteResult(result, arguments.AsJson, arguments.Verbose, nonInteractive);
            PublishCommand.WriteResult(published, arguments.AsJson, nonInteractive);
        }
        if (published.Success && arguments.Open && !string.IsNullOrWhiteSpace(published.Url))
        {
            CliBrowserService.OpenUrl(published.Url);
        }
        return published.Success;
    }

    private static async Task CompletePrivateDeploymentAsync(
        ConfigService config,
        DeployArguments arguments,
        DeploymentPlan plan,
        DeployResult result,
        bool nonInteractive)
    {
        var completed = DeployCommand.AppendDefaultSuccessNextSteps(result);
        DeployCommand.WriteResult(completed, arguments.AsJson, arguments.Verbose, nonInteractive);
        if (!arguments.Open) return;
        var (client, server) = DeployCommand.CreateApiClient(
            config, plan.Directory, plan.EnvironmentName);
        using (client)
        {
            var launchCode = await client.CreateRuntimeLaunchAsync(completed.AppId!);
            CliBrowserService.OpenUrl(CliAppUrlBuilder.BuildRuntimeLaunchUrl(
                server, completed.AppId, launchCode));
        }
    }

    private static void WriteFailure(
        Exception exception, string? targetSlug, bool asJson, bool nonInteractive)
    {
        if (asJson)
        {
            CliJson.Write(new
            {
                success = false,
                summary = "Deploy failed",
                error = exception.Message,
                target = targetSlug
            });
            return;
        }
        if (nonInteractive)
        {
            Console.Error.WriteLine($"Error: {exception.Message}");
            return;
        }
        AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(exception.Message)}");
    }

    private sealed record DeployArguments(
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

    private sealed record DeploymentPlan(
        string Directory,
        string? EnvironmentName,
        DeploymentTargetInfo Target,
        DeploymentTargetService TargetService,
        DeployService DeployService,
        DeployPreparationResult Preparation);

}
