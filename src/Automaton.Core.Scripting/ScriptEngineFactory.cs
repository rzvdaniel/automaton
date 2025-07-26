using IronPython.Hosting;
using Microsoft.Scripting.Hosting;
using System.Text;

namespace Automaton.Core.Scripting
{
    public class ScriptEngineFactory
    {
        private const string PythonScrypt = @"text/x-python";
        private const string BatchScript = @"text/x-batch";

        private readonly Dictionary<string, ScriptRuntime> runtimes;
        private readonly Dictionary<string, IScriptEngine> engines;

        public EventRaisingStreamWriter EventRaisingStreamWriter { get; set; }

        public ScriptEngineFactory()
        {
            EventRaisingStreamWriter = new EventRaisingStreamWriter();

            runtimes = new()
            {
                [PythonScrypt] = Python.CreateRuntime(),
            };

            // Workaround for IronPython output
            runtimes[PythonScrypt].IO.SetOutput(EventRaisingStreamWriter, Encoding.ASCII);

            engines = new()
            {
                [PythonScrypt] = new PythonScriptEngine(Python.GetEngine(runtimes[PythonScrypt])),
                [BatchScript] = new BatchScriptEngine()
            };
        }

        public IScriptEngine GetEngine(string contentType) => engines[contentType];
    }
}