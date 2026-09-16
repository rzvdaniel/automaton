using System.CommandLine;

namespace Automaton.Cli.Services;

internal static class CliOptionValidator
{
    public static bool HasUnsupportedOptions(IEnumerable<string> arguments, Command command)
    {
        var optionsByAlias = command.Options
            .SelectMany(option => option.Aliases.Select(alias => (alias, option)))
            .ToDictionary(pair => pair.alias, pair => pair.option, StringComparer.OrdinalIgnoreCase);
        var tokens = arguments.ToArray();

        for (var index = 0; index < tokens.Length; index++)
        {
            var token = tokens[index];
            if (token == "--")
            {
                break;
            }

            if (!token.StartsWith("-", StringComparison.Ordinal))
            {
                continue;
            }

            var equalsIndex = token.IndexOf('=');
            var alias = equalsIndex < 0 ? token : token[..equalsIndex];
            if (alias is "--help" or "-h" or "-?")
            {
                continue;
            }

            if (!optionsByAlias.TryGetValue(alias, out var option))
            {
                return true;
            }

            if (equalsIndex < 0
                && option.Arity.MaximumNumberOfValues > 0
                && index + 1 < tokens.Length)
            {
                index++;
            }
        }

        return false;
    }
}
