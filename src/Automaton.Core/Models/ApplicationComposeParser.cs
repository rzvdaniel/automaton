using YamlDotNet.RepresentationModel;
using System.Text.Json;

namespace Automaton.Core.Models;

/// <summary>Parses the supported Compose subset into the package's typed contract.</summary>
public static class ApplicationComposeParser
{
    private static readonly string[] FileNames = ["compose.yaml", "compose.yml", "docker-compose.yaml", "docker-compose.yml"];
    private static readonly HashSet<string> RootFields = new(["services", "volumes"], StringComparer.Ordinal);
    private static readonly HashSet<string> ServiceFields = new(
        ["image", "command", "entrypoint", "working_dir", "environment", "depends_on", "ports", "expose", "volumes"],
        StringComparer.Ordinal);

    public static ApplicationComposeSpec? LoadFromDirectory(string directory)
    {
        var paths = FileNames.Select(name => Path.Combine(directory, name)).Where(File.Exists).ToArray();
        if (paths.Length > 1) throw new InvalidOperationException("Only one Compose file may be present.");
        return paths.Length == 0 ? null : Parse(File.ReadAllText(paths[0]));
    }

    public static ApplicationComposeSpec Parse(string yaml)
    {
        var stream = new YamlStream();
        using var reader = new StringReader(yaml);
        stream.Load(reader);
        if (stream.Documents.Count != 1 || stream.Documents[0].RootNode is not YamlMappingNode root)
            throw new InvalidOperationException("Compose must contain one mapping document.");
        RejectUnknownFields(root, RootFields, "Compose");
        var servicesNode = RequiredMapping(root, "services");
        var volumesNode = OptionalMapping(root, "volumes");
        var result = new ApplicationComposeSpec();
        ReadNamedVolumes(volumesNode, result);
        ReadServices(servicesNode, result);
        var contract = new ApplicationContract { Compose = result };
        ApplicationContractValidator.EnsureValid(contract);
        return result;
    }

    private static void ReadNamedVolumes(YamlMappingNode? node, ApplicationComposeSpec compose)
    {
        if (node is null) return;
        foreach (var entry in node.Children)
        {
            var name = Scalar(entry.Key, "volume name");
            if (entry.Value is not YamlMappingNode { Children.Count: 0 })
                throw Unsupported($"volumes.{name}", "volume driver options and external volumes");
            compose.Volumes.Add(name);
        }
    }

    private static void ReadServices(YamlMappingNode node, ApplicationComposeSpec compose)
    {
        foreach (var entry in node.Children)
        {
            var name = Scalar(entry.Key, "service name");
            var serviceNode = entry.Value as YamlMappingNode
                ?? throw Unsupported($"services.{name}", "service values must be mappings");
            compose.Services.Add(ReadService(name, serviceNode, compose));
        }
    }

    private static ApplicationComposeService ReadService(
        string name,
        YamlMappingNode node,
        ApplicationComposeSpec compose)
    {
        RejectUnknownFields(node, ServiceFields, $"services.{name}");
        var service = new ApplicationComposeService
        {
            Name = name,
            Image = Scalar(Required(node, "image"), $"services.{name}.image"),
            Command = OptionalStrings(node, "command"),
            Entrypoint = OptionalStrings(node, "entrypoint"),
            WorkingDirectory = OptionalScalar(node, "working_dir"),
            Environment = OptionalEnvironment(node),
            DependsOn = OptionalStringArray(node, "depends_on"),
            Mounts = OptionalMounts(node)
        };
        SetRoutedPort(node, service, compose);
        _ = OptionalStringArray(node, "expose");
        return service;
    }

    private static void SetRoutedPort(
        YamlMappingNode node,
        ApplicationComposeService service,
        ApplicationComposeSpec compose)
    {
        var ports = OptionalPortArray(node);
        if (ports.Count == 0) return;
        if (ports.Count > 1 || !string.IsNullOrWhiteSpace(compose.PublicService))
            throw Unsupported($"services.{service.Name}.ports", "one externally routed HTTP service with one container port");
        service.Port = ports[0];
        compose.PublicService = service.Name;
    }

    public static bool AreEquivalent(ApplicationComposeSpec? left, ApplicationComposeSpec? right)
        => string.Equals(Canonicalize(left), Canonicalize(right), StringComparison.Ordinal);

    private static string Canonicalize(ApplicationComposeSpec? compose)
    {
        if (compose is null) return "null";
        return JsonSerializer.Serialize(new
        {
            compose.PublicService,
            Services = compose.Services.OrderBy(service => service.Name, StringComparer.Ordinal).Select(service => new
            {
                service.Name,
                service.Image,
                service.Port,
                service.Command,
                service.Entrypoint,
                service.WorkingDirectory,
                Environment = service.Environment.OrderBy(pair => pair.Key, StringComparer.Ordinal),
                DependsOn = service.DependsOn.OrderBy(name => name, StringComparer.Ordinal),
                Mounts = service.Mounts.OrderBy(mount => mount.Target, StringComparer.Ordinal)
            }),
            Volumes = compose.Volumes.OrderBy(name => name, StringComparer.Ordinal)
        });
    }

    private static List<ApplicationComposeVolumeMount> OptionalMounts(YamlMappingNode node)
    {
        if (!node.Children.TryGetValue(Key("volumes"), out var value)) return [];
        if (value is not YamlSequenceNode sequence) throw Unsupported("services.*.volumes", "managed named volumes only");
        var mounts = new List<ApplicationComposeVolumeMount>();
        foreach (var item in sequence.Children)
        {
            var parts = Scalar(item, "service volume").Split(':');
            if (parts.Length is < 2 or > 3 || parts[1].Length == 0)
                throw Unsupported("services.*.volumes", "short-form managed named volumes only");
            var source = parts[0];
            if (source.Contains('/') || source.Contains('\\') || source == "." || source == "..")
                throw Unsupported("services.*.volumes", "host mounts");
            var readOnly = parts.Length == 3 && parts[2] == "ro";
            if (parts.Length == 3 && parts[2] is not ("ro" or "rw"))
                throw Unsupported("services.*.volumes", "managed named volumes with ro or rw mode");
            mounts.Add(new ApplicationComposeVolumeMount { Name = source, Target = parts[1], ReadOnly = readOnly });
        }
        return mounts;
    }

    private static List<int> OptionalPortArray(YamlMappingNode node)
    {
        if (!node.Children.TryGetValue(Key("ports"), out var value)) return [];
        var items = value is YamlSequenceNode sequence ? sequence.Children : [value];
        var ports = new List<int>();
        foreach (var item in items)
        {
            var text = Scalar(item, "service port");
            if (!int.TryParse(text, out var port) || port is < 1 or > 65535)
                throw Unsupported("services.*.ports", "container ports without host port mappings");
            ports.Add(port);
        }
        return ports;
    }

    private static Dictionary<string, string> OptionalEnvironment(YamlMappingNode node)
    {
        var mapping = OptionalMapping(node, "environment");
        if (mapping is null) return new(StringComparer.Ordinal);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in mapping.Children)
        {
            var name = Scalar(pair.Key, "environment variable name");
            var value = Scalar(pair.Value, $"environment.{name}");
            if (value.Contains('$')) throw Unsupported("services.*.environment", "Compose variable interpolation; use non-sensitive literal configuration");
            result.Add(name, value);
        }
        return result;
    }

    private static List<string> OptionalStrings(YamlMappingNode node, string key)
    {
        if (!node.Children.TryGetValue(Key(key), out var value)) return [];
        if (value is YamlSequenceNode sequence) return sequence.Children.Select(item => Scalar(item, $"services.*.{key}")).ToList();
        throw Unsupported($"services.*.{key}", "a string array; scalar forms are not supported");
    }

    private static List<string> OptionalStringArray(YamlMappingNode node, string key)
    {
        if (!node.Children.TryGetValue(Key(key), out var value)) return [];
        if (value is not YamlSequenceNode sequence) throw Unsupported($"services.*.{key}", "a string array");
        return sequence.Children.Select(item => Scalar(item, $"services.*.{key}")).ToList();
    }

    private static string? OptionalScalar(YamlMappingNode node, string key)
        => node.Children.TryGetValue(Key(key), out var value) ? Scalar(value, $"services.*.{key}") : null;

    private static YamlMappingNode RequiredMapping(YamlMappingNode node, string key)
        => Required(node, key) as YamlMappingNode ?? throw Unsupported(key, "a mapping");

    private static YamlMappingNode? OptionalMapping(YamlMappingNode node, string key)
    {
        if (!node.Children.TryGetValue(Key(key), out var value)) return null;
        return value as YamlMappingNode ?? throw Unsupported(key, "a mapping");
    }

    private static YamlNode Required(YamlMappingNode node, string key)
        => node.Children.TryGetValue(Key(key), out var value) ? value : throw Unsupported(key, "a required field");

    private static string Scalar(YamlNode node, string field)
    {
        if (node is not YamlScalarNode scalar || scalar.Value is null || scalar.Value.Contains('$'))
            throw Unsupported(field, "a literal scalar without Compose interpolation");
        return scalar.Value;
    }

    private static void RejectUnknownFields(YamlMappingNode node, ISet<string> allowed, string parent)
    {
        var fields = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in node.Children)
        {
            var key = Scalar(entry.Key, $"{parent} field");
            if (!fields.Add(key)) throw Unsupported(parent, "unique fields");
            if (!allowed.Contains(key)) throw Unsupported($"{parent}.{key}", "the documented Compose subset");
        }
    }

    private static YamlScalarNode Key(string value) => new(value);
    private static InvalidOperationException Unsupported(string field, string supported)
        => new($"Unsupported Compose field '{field}'; supported: {supported}.");
}
