using Automaton.Core.Models;

namespace Automaton.Studio.Domain
{
    public class StringStepVariable : StepVariable
    {
        public string StringValue { get; set; }

        public override string? Value
        {
            get => StringValue;
            set => StringStepVariable_set(value);
        }

        private void StringStepVariable_set(string? value)
        {
            StringValue = value ?? string.Empty;
        }
    }
}
