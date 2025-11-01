using Automaton.Core.Models;
using System.Globalization;

namespace Automaton.Steps;

public class If : Sequence
{
    // The step expects a single string input named 'Expression' that already contains
    // a simple truthy/falsey value like: true/false,1/0, yes/no, on/off, y/n, enabled/disabled
    // No complex expression parsing is performed.

    public StepVariable? Expression { get; set; }

    protected override Task<ExecutionResult> RunAsync(StepExecutionContext context)
    {
        Expression = GetInputVariable(nameof(Expression));

        var result = ExpressionIsTrue(context.Workflow);

        NextStepId = result ? NextStepId : SequenceEndStepId;

        return Task.FromResult(ExecutionResult.Next());
    }

    private bool ExpressionIsTrue(Workflow workflow)
    {
        var value = Expression?.Value;
        return IsTruthy(value);
    }

    private static bool IsTruthy(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;

        var v = s.Trim();

        // Booleans
        if (bool.TryParse(v, out var b)) return b;

        // Numeric truthiness (non-zero)
        if (double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
            return Math.Abs(d) > double.Epsilon;

        // Common textual truthy/falsey values (case-insensitive)
        switch (v.ToLowerInvariant())
        {
            case "1":
            case "yes":
            case "y":
            case "on":
            case "ok":
            case "enable":
            case "enabled":
            case "t":
            case "true":
                return true;

            case "0":
            case "no":
            case "n":
            case "off":
            case "disable":
            case "disabled":
            case "f":
            case "false":
            case "none":
            case "null":
                return false;
        }

        // Default: any non-empty string other than explicit falsey values is considered true
        return true;
    }
}
