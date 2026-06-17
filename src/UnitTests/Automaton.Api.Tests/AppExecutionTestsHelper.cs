using Automaton.Api.Models;
using Automaton.Core.Models;

namespace Automaton.Api.Tests;

public static class AppExecutionTestsHelper
{
    public static List<string> ApplyInputVariables(Flow flow, Dictionary<string, string> inputs)
    {
        var missing = new List<string>();

        if (inputs == null || inputs.Count == 0)
            return missing;

        foreach (var input in inputs)
        {
            var matchingVar = flow.InputVariables.Values
                .FirstOrDefault(v => v.Name.Equals(input.Key, StringComparison.OrdinalIgnoreCase));

            if (matchingVar != null)
            {
                matchingVar.Value = input.Value;
            }
            else
            {
                missing.Add(input.Key);
            }
        }

        return missing;
    }

    public static List<Guid> SelectRunners(IEnumerable<RunnerDetails> onlineRunners, List<string>? requestedNames, Guid? executionRunnerId = null)
    {
        if (requestedNames != null && requestedNames.Any())
        {
            var requestedSet = new HashSet<string>(requestedNames, StringComparer.OrdinalIgnoreCase);
            var matched = onlineRunners.Where(r => requestedSet.Contains(r.Name) && r.Status == Automaton.Core.Enums.RunnerStatus.Online).Select(r => r.Id).ToList();
            if (!matched.Any()) throw new InvalidOperationException("None of the specified runners are online");
            return matched;
        }

        if (executionRunnerId != null)
        {
            var runner = onlineRunners.FirstOrDefault(r => r.Id == executionRunnerId.Value);
            if (runner == null || runner.Status != Automaton.Core.Enums.RunnerStatus.Online)
                throw new InvalidOperationException("Configured execution runner is not online");
            return new List<Guid> { executionRunnerId.Value };
        }

        throw new InvalidOperationException("No runner specified. Provide RunnerNames or set an execution runner on the app.");
    }
}
