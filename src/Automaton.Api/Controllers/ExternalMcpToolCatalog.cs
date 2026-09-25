namespace Automaton.Api.Controllers;

internal static class ExternalMcpToolCatalog
{
    public static object List() => new
    {
        tools = new object[]
        {
            ToolDef("create_app",
                "Create a new Automaton app. Returns the appId. Immediately write the returned appId to 'automaton.json' in the project root using your local file tools so it persists across sessions.",
                Props(
                    Prop("name", "string", "Human-readable name for the app")),
                ["name"]),

            ToolDef("list_user_apps",
                "List the current user's accessible apps so you can find an existing appId before using read_file or list_files.",
                new { type = "object", properties = new { }, required = Array.Empty<string>() }),

            ToolDef("read_file",
                "Read a file (or a line range within it) from the Automaton app. Always returns a header showing total lines.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        appId     = new { type = "string",  description = "App ID — read from automaton.json" },
                        path      = new { type = "string",  description = "Relative file path" },
                        startLine = new { type = "integer", description = "1-based first line to return (default: 1)" },
                        endLine   = new { type = "integer", description = "1-based last line to return, inclusive (default: end of file)" }
                    },
                    required = new[] { "appId", "path" }
                }),

            ToolDef("list_files",
                "List editable source files currently stored for the Automaton app.",
                Props(
                    Prop("appId", "string", "App ID — read from automaton.json")),
                ["appId"]),

            ToolDef("get_file_tree",
                "Return the editable source file tree for the Automaton app as hierarchical JSON with folders and files.",
                Props(
                    Prop("appId", "string", "App ID — read from automaton.json")),
                ["appId"]),

            ToolDef("list_runners",
                "List the runners available to the current user.",
                new { type = "object", properties = new { }, required = Array.Empty<string>() })
        }
    };

    private static object ToolDef(string name, string description, object inputSchema) =>
        new { name, description, inputSchema };

    private static object ToolDef(string name, string description, object properties, string[] required) =>
        new { name, description, inputSchema = new { type = "object", properties, required } };

    private static object Props(params object[] props) => MergeProps(props);

    private static object Prop(string name, string type, string description) =>
        new { name, schema = new { type, description } };

    private static Dictionary<string, object> MergeProps(object[] props)
    {
        var dict = new Dictionary<string, object>();
        foreach (dynamic p in props)
            dict[p.name] = p.schema;
        return dict;
    }

}
