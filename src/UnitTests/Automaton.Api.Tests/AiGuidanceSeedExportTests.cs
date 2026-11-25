using Automaton.AiGuidance;
using Automaton.Api.Services;
using System.Security.Cryptography;
using System.Text;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class AiGuidanceSeedExportTests
{
    [Test]
    public void SeedCatalog_ExplicitlyRegistersEveryEmbeddedSeed()
    {
        var provider = new EmbeddedAutomatonGuidanceProvider();
        var embeddedPaths = provider.GetSeedSources().Select(source => source.RelativePath).ToArray();
        var catalogPaths = AutomatonGuidanceSeedCatalog.GetEntries().Select(entry => entry.RelativePath).ToArray();

        Assert.That(embeddedPaths, Is.EqualTo(catalogPaths.OrderBy(path => path, StringComparer.Ordinal).ToArray()));
        Assert.That(catalogPaths, Has.Length.EqualTo(6));
        Assert.That(
            AutomatonGuidanceSeedCatalog.GetEntries()
                .Where(entry => entry.OutputPath is not null)
                .Select(entry => entry.RelativePath)
                .OrderBy(path => path, StringComparer.Ordinal),
            Is.EqualTo(new[]
            {
                "cli/project-files/.automaton/agents/skills/automaton-app/SKILL.md",
                "cli/project-files/AGENTS.md"
            }));
        Assert.That(
            AutomatonGuidanceSeedCatalog.GetEntries()
                .Where(entry => entry.Surface != AutomatonGuidanceSeedCatalog.CliSurface)
                .All(entry => entry.OutputPath is null),
            Is.True);
        Assert.That(() => AutomatonGuidanceSeedCatalog.Resolve("shared/guidance/unknown.md"), Throws.InvalidOperationException);
    }

    [Test]
    public async Task BuildExportPackAsync_ExportsExactlyTheTwoCliProjectFiles()
    {
        var service = CreateRuntimeService();

        var export = await service.BuildExportPackAsync();
        var files = export.Files.ToDictionary(file => file.TargetPath, file => file.Content, StringComparer.Ordinal);

        Assert.That(files.Keys.OrderBy(path => path, StringComparer.Ordinal).ToArray(), Is.EqualTo(new[]
        {
            ".automaton/agents/skills/automaton-app/SKILL.md",
            "AGENTS.md"
        }));

        var exportedText = string.Join("\n", files.Values);
        var normalizedExportedText = exportedText.ReplaceLineEndings(" ");
        Assert.Multiple(() =>
        {
            Assert.That(exportedText, Does.Contain("Dynamic HTTP apps must declare"));
            Assert.That(exportedText, Does.Contain("Static sites use a build command"));
            Assert.That(normalizedExportedText, Does.Contain("shared static server"));
            Assert.That(normalizedExportedText, Does.Contain("Actions-only or other finite-work"));
            Assert.That(exportedText, Does.Contain("no HTTP endpoint"));
            Assert.That(exportedText, Does.Contain("automaton create"));
            Assert.That(exportedText, Does.Contain("not linked in the current environment"));
            Assert.That(normalizedExportedText, Does.Contain("WebSocket upgrades are relayed too."));
            Assert.That(normalizedExportedText, Does.Contain("Server-Sent Events"));
            Assert.That(normalizedExportedText, Does.Not.Contain("are not supported. For live updates"));
            Assert.That(exportedText, Does.Not.Contain("automaton target approve"));
            Assert.That(exportedText, Does.Not.Contain("When the app has no server-side process"));

            foreach (var staleTerm in new[]
            {
                "\"frontend\":",
                "\"backend\":",
                "main:app",
                "FastAPI",
                "Default to frontend-only",
                "frontend/",
                "backend/",
                "Vite",
                "React",
                "Vue"
            })
            {
                Assert.That(exportedText, Does.Not.Contain(staleTerm), staleTerm);
            }

            foreach (var requiredTerm in new[]
            {
                "application",
                "build",
                "runtime",
                "health",
                "storage",
                "automaton validate",
                "automaton deploy",
                "Succeeded",
                "publish"
            })
            {
                Assert.That(exportedText, Does.Contain(requiredTerm), requiredTerm);
            }
        });
    }

    [Test]
    public async Task GuidanceOutput_IsByteForByteTheOutputOfTheDatabaseBackedRuntime()
    {
        // SHA-256 of the UTF-8 content captured from the previous database-backed runtime.
        var expected = new[]
        {
            "export|AGENTS.md|ee4adf4d7fe551893af63169db6df5e8b2a5e05973f314fae2f6cbe85599c048",
            "export|.automaton/agents/skills/automaton-app/SKILL.md|630f6010d38c224ff2b8555d477dba75efa46b07c0e2cb1f0c4b92a7b5fbed9b",
            "base|shared|automaton-app-contract|531014b837b6f3aed563bb9dab285e7d723a8f16500d97b6dd7ac4e5847fcecf",
            "req|studio|automaton-studio-editor|2933c8cf7b268f92e1b3bf9388d08a572a1e5d9e6b64bf9b423bf9eb3aeee93e",
            "key|automaton-mcp|4371767fb9d3f03821b2c872080b3f26d9c7bc1f4ebe4f6b0f8befc8dbc62937",
            "key|automaton-actions|63bfb3cae8f5c1c424631e2e874a7fda299ab795d4e686a267b78976b42c3334",
            "key|automaton-studio-editor|2933c8cf7b268f92e1b3bf9388d08a572a1e5d9e6b64bf9b423bf9eb3aeee93e"
        };
        var service = CreateRuntimeService();
        IAutomatonGuidanceRuntimeProvider provider = service;
        var actual = new List<string>();
        foreach (var file in (await service.BuildExportPackAsync()).Files)
        {
            actual.Add($"export|{file.TargetPath}|{Hash(file.Content)}");
        }

        actual.Add($"base|shared|automaton-app-contract|{Hash((await provider.GetBasePromptAsync("shared"))!.ContentMarkdown)}");
        actual.AddRange((await provider.GetRequiredGuidanceAsync("studio")).Select(item => $"req|studio|{item.Key}|{Hash(item.ContentMarkdown)}"));
        foreach (var item in await provider.ListLoadableGuidanceAsync())
        {
            actual.Add($"key|{item.Key}|{Hash((await provider.GetGuidanceByKeyAsync(item.Key))!.ContentMarkdown)}");
        }

        Assert.That(actual, Is.EqualTo(expected));
        Assert.That(
            (await provider.ListLoadableGuidanceAsync("mcp")).Select(item => (item.Key, item.Title, item.Description, item.SortOrder, item.TagsJson)),
            Is.EqualTo(new[]
            {
                ("automaton-mcp", "Automaton MCP", "How Claude connects to an Automaton server over MCP, what the tools do and when to use the CLI.", 100, "[\"mcp\",\"tools\"]")
            }));
        Assert.That(await provider.GetGuidanceByKeyAsync("automaton-app-contract"), Is.Null);
        Assert.That((await provider.GetGuidanceByKeyAsync("Automaton-MCP"))?.Key, Is.EqualTo("automaton-mcp"));
        Assert.That((await provider.ListLoadableGuidanceAsync("MCP")).Select(item => item.Key), Is.EqualTo(new[] { "automaton-mcp" }));
    }

    private static string Hash(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

    private static AiGuidanceRuntimeService CreateRuntimeService() =>
        new(new EmbeddedAutomatonGuidanceProvider());
}
