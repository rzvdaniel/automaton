using Automaton.Core.Models;
using System.Text;
using System.Text.Json;
using YamlDotNet.RepresentationModel;

namespace Automaton.Cli.Services;

/// <summary>
/// Turns a Docker Compose file into an app folder: automaton.json plus a compose.yaml reduced to the subset Automaton
/// runs. What cannot be carried over (restart policies, host port mappings, networks) is dropped and reported, never
/// guessed; what cannot be dropped safely (build contexts, host mounts, interpolation) stops the import.
/// </summary>
internal static class ComposeImportService
{
    private static readonly HashSet<string> Kept = new(["image", "command", "entrypoint", "working_dir", "environment", "depends_on", "volumes"], StringComparer.Ordinal);
    private static readonly HashSet<string> Dropped = new(["restart", "container_name", "networks", "healthcheck", "labels", "hostname", "stdin_open", "tty", "logging", "profiles", "pull_policy", "platform", "mem_limit", "cpus", "deploy", "env_file"], StringComparer.Ordinal);

    internal sealed record Result(string ComposeYaml, string ManifestJson, IReadOnlyList<string> Notes);

    internal static Result Import(string composeYaml, string appName)
    {
        var notes = new List<string>();
        var stream = new YamlStream();
        stream.Load(new StringReader(composeYaml));
        if (stream.Documents.Count != 1 || stream.Documents[0].RootNode is not YamlMappingNode root)
            throw new InvalidOperationException("Compose must contain one mapping document.");

        var output = new YamlMappingNode();
        foreach (var entry in root.Children)
        {
            var key = Text(entry.Key);
            if (key == "services" || key == "volumes") continue;
            notes.Add($"Dropped top-level '{key}' (not part of the supported subset).");
        }

        var services = root.Children.TryGetValue(new YamlScalarNode("services"), out var s) && s is YamlMappingNode m ? m : throw new InvalidOperationException("Compose needs a 'services' mapping.");
        var outServices = new YamlMappingNode();
        var routed = new List<string>();
        foreach (var service in services.Children)
        {
            var name = Text(service.Key);
            var node = service.Value as YamlMappingNode ?? throw new InvalidOperationException($"services.{name} must be a mapping.");
            outServices.Add(name, Service(name, node, notes, routed));
        }
        output.Add("services", outServices);
        if (routed.Count > 1)
            throw new InvalidOperationException($"More than one service publishes a port ({string.Join(", ", routed)}). Automaton routes one HTTP service; remove the other ports and import again.");
        if (routed.Count == 0)
            notes.Add("No service publishes a port, so nothing is reachable. Add a 'ports' entry to the web service and import again.");

        if (root.Children.TryGetValue(new YamlScalarNode("volumes"), out var volumes) && volumes is YamlMappingNode vm)
        {
            var outVolumes = new YamlMappingNode();
            foreach (var volume in vm.Children)
            {
                if (volume.Value is not (YamlMappingNode { Children.Count: 0 } or YamlScalarNode { Value: null or "" }))
                    throw new InvalidOperationException($"volumes.{Text(volume.Key)} has options or is external; only plain named volumes are supported.");
                outVolumes.Add(Text(volume.Key), new YamlMappingNode());
            }
            output.Add("volumes", outVolumes);
        }

        var yaml = Serialize(output);
        ApplicationComposeParser.Parse(yaml);

        var manifest = new Dictionary<string, object>
        {
            ["name"] = appName,
            ["summary"] = $"{appName}, imported from a Docker Compose file. Replace this line with what the app does.",
            ["application"] = new Dictionary<string, object>
            {
                ["health"] = new Dictionary<string, object> { ["path"] = "/" },
            }
        };
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }) + "\n";
        return new Result(yaml, json, notes);
    }

    private static YamlMappingNode Service(string name, YamlMappingNode node, List<string> notes, List<string> routed)
    {
        var result = new YamlMappingNode();
        if (node.Children.ContainsKey(new YamlScalarNode("build")))
            throw new InvalidOperationException($"services.{name} uses 'build'. Automaton runs published images; push the image and use 'image'.");
        foreach (var entry in node.Children)
        {
            var key = Text(entry.Key);
            if (key == "ports") { Ports(name, entry.Value, result, notes, routed); continue; }
            if (key == "expose") { notes.Add($"services.{name}: dropped 'expose' (not needed)."); continue; }
            if (Kept.Contains(key)) { result.Add(key, Normalize(key, entry.Value)); continue; }
            if (Dropped.Contains(key)) { notes.Add($"services.{name}: dropped '{key}'."); continue; }
            throw new InvalidOperationException($"services.{name}.{key} is not supported by Automaton Compose apps and cannot be dropped safely.");
        }
        return result;
    }

    // A published port "8080:80", "127.0.0.1:8080:80/tcp" or "80" becomes the container port alone.
    private static void Ports(string name, YamlNode value, YamlMappingNode result, List<string> notes, List<string> routed)
    {
        var items = value is YamlSequenceNode sequence ? sequence.Children.ToList() : [value];
        var containerPorts = new List<string>();
        foreach (var item in items)
        {
            var text = Text(item);
            var container = text.Split('/')[0].Split(':').Last();
            if (!int.TryParse(container, out var port) || port is < 1 or > 65535)
                throw new InvalidOperationException($"services.{name}.ports: '{text}' is not a simple port mapping.");
            containerPorts.Add(port.ToString());
        }
        if (containerPorts.Count == 0) return;
        if (containerPorts.Count > 1)
            notes.Add($"services.{name}: publishes {containerPorts.Count} ports; kept the first ({containerPorts[0]}) as the routed HTTP port.");
        result.Add("ports", new YamlSequenceNode(new YamlScalarNode(containerPorts[0])));
        routed.Add(name);
    }

    private static YamlNode Normalize(string key, YamlNode value)
    {
        // environment as a list ("A=b") becomes a mapping; depends_on as a mapping becomes a list of names.
        if (key == "environment" && value is YamlSequenceNode list)
        {
            var map = new YamlMappingNode();
            foreach (var item in list.Children)
            {
                var text = Text(item);
                var at = text.IndexOf('=');
                if (at <= 0) throw new InvalidOperationException($"environment entry '{text}' needs a value (NAME=value); values from your shell are not carried over.");
                map.Add(text[..at], text[(at + 1)..]);
            }
            return map;
        }
        if (key == "depends_on" && value is YamlMappingNode dependencies)
            return new YamlSequenceNode(dependencies.Children.Select(c => (YamlNode)new YamlScalarNode(Text(c.Key))));
        if (key is "command" or "entrypoint" && value is YamlScalarNode scalar)
            throw new InvalidOperationException($"'{key}: {scalar.Value}' is a single string; write it as a list, for example [\"cmd\", \"arg\"].");
        return value;
    }

    private static string Text(YamlNode node) => node is YamlScalarNode { Value: not null } scalar ? scalar.Value : throw new InvalidOperationException("Expected a plain value in the Compose file.");

    private static string Serialize(YamlMappingNode root)
    {
        var builder = new StringBuilder();
        using var writer = new StringWriter(builder);
        new YamlStream(new YamlDocument(root)).Save(writer, assignAnchors: false);
        return builder.ToString().Replace("...\n", "").Replace("...\r\n", "");
    }
}
