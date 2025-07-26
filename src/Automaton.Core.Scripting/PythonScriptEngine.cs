using Microsoft.Scripting.Hosting;
using System.Collections.Generic;

namespace Automaton.Core.Scripting
{
    public class PythonScriptEngine : IScriptEngine
    {
        private readonly ScriptEngine engine;

        public PythonScriptEngine(ScriptEngine engine)
        {
            this.engine = engine;
        }

        public IDictionary<string, object> Execute(string code, IDictionary<string, object> inputs)
        {
            var scope = engine.CreateScope();
            foreach (var kvp in inputs)
            {
                scope.SetVariable(kvp.Key, kvp.Value);
            }

            var variables = new Dictionary<string, object>();

            try
            {
                engine.Execute(code, scope);

                var items = scope.GetItems();
                foreach (var item in items)
                {
                    variables[item.Key] = item.Value;
                }
            }
            catch (Exception ex)
            {
                variables["error"] = ex.Message;
                variables["stackTrace"] = ex.StackTrace;
                throw;
            }

            return variables;
        }
    }
}