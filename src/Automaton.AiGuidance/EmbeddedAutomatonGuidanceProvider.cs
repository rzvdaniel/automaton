using System.Reflection;
using System.Text;

namespace Automaton.AiGuidance;

public sealed class EmbeddedAutomatonGuidanceProvider : IAutomatonGuidanceProvider
{
    private const string ReferencePrefix = "Automaton.AiGuidance.references.";
    private const string TemplatePrefix = "Automaton.AiGuidance.templates.";

    private static readonly IReadOnlyDictionary<string, string> ReferenceMap = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["llms.txt"] = "llms.txt",
        ["llms-full.txt"] = "llms-full.txt",
        ["llms.automaton-app-contract-compact.md"] = "llms/automaton-app-contract-compact.md",
        ["llms.automaton-backend-controllers.md"] = "llms/automaton-backend-controllers.md",
        ["llms.automaton-cli-deploy.md"] = "llms/automaton-cli-deploy.md",
        ["llms.automaton-cli-install.md"] = "llms/automaton-cli-install.md",
        ["llms.automaton-examples.md"] = "llms/automaton-examples.md",
        ["llms.automaton-frontend-structure.md"] = "llms/automaton-frontend-structure.md",
        ["llms.automaton-mcp.md"] = "llms/automaton-mcp.md",
        ["llms.automaton-media-assets.md"] = "llms/automaton-media-assets.md",
        ["llms.automaton-starter-prompts.md"] = "llms/automaton-starter-prompts.md",
        ["llms.automaton-studio-editor.md"] = "llms/automaton-studio-editor.md"
    };

    private static readonly IReadOnlyDictionary<string, string> TemplateMap = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["AGENTS.md"] = "AGENTS.md",
        ["instructions.md"] = "instructions.md",
        ["SKILL.md"] = "SKILL.md",
        ["adapter.md"] = "adapter.md",
        ["github-skill.md"] = "github-skill.md",
        ["gitignore"] = "gitignore"
    };

    private readonly Assembly _assembly = typeof(EmbeddedAutomatonGuidanceProvider).Assembly;
    private readonly Lazy<IReadOnlyList<GuidanceSource>> _referenceSources;
    private readonly Lazy<IReadOnlyList<GuidanceSource>> _templateSources;

    public EmbeddedAutomatonGuidanceProvider()
    {
        _referenceSources = new Lazy<IReadOnlyList<GuidanceSource>>(() => LoadSources(ReferenceMap, ReferencePrefix));
        _templateSources = new Lazy<IReadOnlyList<GuidanceSource>>(() => LoadSources(TemplateMap, TemplatePrefix));
    }

    public string GetLlms() => GetReference("llms.txt");

    public string GetLlmsFull() => GetReference("llms-full.txt");

    public string GetReference(string relativePath) => GetSourceContent(GetReferenceSources(), relativePath, "reference");

    public string GetTemplate(string relativePath) => GetSourceContent(GetTemplateSources(), relativePath, "template");

    public IReadOnlyList<GuidanceSource> GetReferenceSources() => _referenceSources.Value;

    public IReadOnlyList<GuidanceSource> GetTemplateSources() => _templateSources.Value;

    private static string GetSourceContent(IReadOnlyList<GuidanceSource> sources, string relativePath, string kind)
    {
        var source = sources.FirstOrDefault(item => string.Equals(item.RelativePath, relativePath, StringComparison.Ordinal));
        if (source is null)
        {
            throw new InvalidOperationException($"Embedded AI guidance {kind} '{relativePath}' was not found.");
        }

        return source.Content;
    }

    private IReadOnlyList<GuidanceSource> LoadSources(IReadOnlyDictionary<string, string> sourceMap, string prefix)
    {
        return sourceMap
            .Select(entry =>
            {
                using var stream = _assembly.GetManifestResourceStream(prefix + entry.Key)
                    ?? throw new InvalidOperationException($"Embedded AI guidance resource '{prefix + entry.Key}' was not found.");
                using var reader = new StreamReader(stream, Encoding.UTF8);
                return new GuidanceSource(entry.Value, reader.ReadToEnd());
            })
            .ToList();
    }
}
