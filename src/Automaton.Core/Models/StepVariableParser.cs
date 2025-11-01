using Serilog;
using System.Text.RegularExpressions;

namespace Automaton.Core.Models
{
    public class StepVariableParser
    {
        private const string Percentage = "%";
        private const string VariablePattern = "%.+?%";

        protected readonly ILogger logger;

        public StepVariableParser(ILogger logger)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public StepVariableParser() : this(Log.ForContext<StepVariableParser>())
        {
        }

        // String-only parser: replaces %var% with string values from workflow variables
        public string Parse(StepVariable variable, Workflow workflow)
        {
            var stringExpression = variable.Value ?? string.Empty;
            var variableNames = GetVariableNames(stringExpression);

            return ParseString(stringExpression, variableNames, workflow);
        }

        private string ParseString(string expression, IEnumerable<string> variableNames, Workflow workflow)
        {
            foreach (var variableName in variableNames)
            {
                var variable = workflow.GetVariableByName(variableName);

                if (variable == null)
                {
                    logger.Error("Variable {0} does not exist", variableName);
                    continue;
                }

                var value = variable.Value ?? string.Empty;
                expression = expression.Replace($"%{variableName}%", value);
            }

            return expression;
        }

        private static IEnumerable<string> GetVariableNames(string inputString)
        {
            var regex = new Regex(VariablePattern, RegexOptions.IgnoreCase);
            var matches = regex.Matches(inputString);
            var variableNames = matches.Select(x => x.Value.Replace(Percentage, string.Empty));

            return variableNames;
        }
    }
}
