using System.Reflection;
using System.Text;

namespace Automaton.AiGuidance;

public sealed class EmbeddedAutomatonGuidanceProvider : IAutomatonGuidanceProvider
{
    private const string SeedPrefix = "Automaton.AiGuidance.seed";

    private readonly Assembly _assembly = typeof(EmbeddedAutomatonGuidanceProvider).Assembly;
    private readonly Lazy<IReadOnlyList<GuidanceSource>> _seedSources;

    public EmbeddedAutomatonGuidanceProvider()
    {
        _seedSources = new Lazy<IReadOnlyList<GuidanceSource>>(LoadSeedSources);
    }

    public IReadOnlyList<GuidanceSource> GetSeedSources() => _seedSources.Value;

    private IReadOnlyList<GuidanceSource> LoadSeedSources()
    {
        return _assembly
            .GetManifestResourceNames()
            .Where(name => name.StartsWith(SeedPrefix, StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name =>
            {
                using var stream = _assembly.GetManifestResourceStream(name)
                    ?? throw new InvalidOperationException($"Embedded AI guidance resource '{name}' was not found.");
                using var reader = new StreamReader(stream, Encoding.UTF8);
                return new GuidanceSource(
                    NormalizeRelativePath(name[SeedPrefix.Length..]),
                    reader.ReadToEnd());
            })
            .ToList();
    }

    private static string NormalizeRelativePath(string suffix)
    {
        var trimmed = suffix.TrimStart('.', '\\', '/');
        return trimmed.Replace('\\', '/');
    }
}
