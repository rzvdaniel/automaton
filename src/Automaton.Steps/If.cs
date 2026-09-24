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

        var result = IsTruthy(Expression?.Value);

        NextStepId = result ? NextStepId : SequenceEndStepId;

        return Task.FromResult(ExecutionResult.Next());
    }

    private static readonly HashSet<string> ExplicitFalseValues = new(StringComparer.Ordinal)
    {
        "no", "n", "off", "disable", "disabled", "f", "none", "null"
    };

    private static bool IsTruthy(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var text = value.Trim();
        if (bool.TryParse(text, out var boolean))
            return boolean;

        if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var number))
            return Math.Abs(number) > double.Epsilon;

        // Every other non-empty value is true, except for explicit false aliases.
        return !ExplicitFalseValues.Contains(text.ToLowerInvariant());
    }
}
