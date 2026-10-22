using Automaton.Core.Models;

namespace Automaton.Core.Tests;

public class AppActionManifestParserTests
{
    private const string ValidManifest = """
        apiVersion: automaton.dev/v1
        kind: Action
        metadata:
          name: Hello
          key: hello
        spec:
          runtime: python
          entrypoint: main.py
          timeoutSeconds: 60
        """;

    [Test]
    public void ParsesValidManifest()
    {
        var manifest = AppActionManifestParser.Parse(ValidManifest, "hello");

        Assert.That(manifest.Metadata.Key, Is.EqualTo("hello"));
        Assert.That(manifest.Spec.Runtime, Is.EqualTo(AppActionRuntimes.Python));
        Assert.That(manifest.Spec.Entrypoint, Is.EqualTo("main.py"));
        Assert.That(manifest.Spec.TimeoutSeconds, Is.EqualTo(60));
    }

    [Test]
    public void ParsesExplicitSchedulingPolicy()
    {
        var yaml = ValidManifest.Replace(
            "timeoutSeconds: 60",
            """
            timeoutSeconds: 60
              schedulable: true
              allowedExecutionScopes:
                - AppRunner
              defaultConcurrencyPolicy: Queue
              minimumIntervalSeconds: 600
            """);

        var manifest = AppActionManifestParser.Parse(yaml, "hello");

        Assert.Multiple(() =>
        {
            Assert.That(manifest.Spec.Schedulable, Is.True);
            Assert.That(manifest.Spec.AllowedExecutionScopes, Is.EqualTo(new[] { "AppRunner" }));
            Assert.That(manifest.Spec.DefaultConcurrencyPolicy, Is.EqualTo("Queue"));
            Assert.That(manifest.Spec.MinimumIntervalSeconds, Is.EqualTo(600));
        });
    }

    [Test]
    public void RejectsSchedulableManifestWithoutAllowedScope()
    {
        var yaml = ValidManifest.Replace("timeoutSeconds: 60", "timeoutSeconds: 60\n  schedulable: true");
        Assert.That(() => AppActionManifestParser.Parse(yaml, "hello"), Throws.InvalidOperationException);
    }

    [TestCase("../main.py")]
    [TestCase("/main.py")]
    [TestCase("nested//main.py")]
    public void RejectsUnsafeEntrypoint(string entrypoint)
    {
        var yaml = ValidManifest.Replace("main.py", entrypoint);

        var error = Assert.Throws<InvalidOperationException>(() => AppActionManifestParser.Parse(yaml, "hello"))!;

        Assert.That(error.Message, Does.Contain("safe relative path"));
    }

    [Test]
    public void RejectsFolderAndManifestKeyMismatch()
    {
        var error = Assert.Throws<InvalidOperationException>(() => AppActionManifestParser.Parse(ValidManifest, "other"))!;

        Assert.That(error.Message, Does.Contain("folder name"));
    }

    [Test]
    public void RejectsPrivilegedCapabilitiesForNormalActions()
    {
        var yaml = ValidManifest + "\n  capabilities:\n    - docker\n";

        var error = Assert.Throws<InvalidOperationException>(() => AppActionManifestParser.Parse(yaml, "hello"))!;

        Assert.That(error.Message, Does.Contain("privileged"));
    }

    [Test]
    public void RejectsUnknownCapabilitiesEvenForSystemActions()
    {
        var yaml = ValidManifest + "\n  capabilities:\n    - unrestricted-network\n";

        var error = Assert.Throws<InvalidOperationException>(() => AppActionManifestParser.Parse(yaml, "hello", allowPrivilegedCapabilities: true))!;

        Assert.That(error.Message, Does.Contain("not supported"));
    }

    [Test]
    public void DiscoversOnlyDirectActionDirectories()
    {
        var root = Path.Combine(Path.GetTempPath(), "automaton-actions", Guid.NewGuid().ToString("N"));
        var action = Path.Combine(root, "actions", "hello");
        Directory.CreateDirectory(action);
        File.WriteAllText(Path.Combine(action, "action.yaml"), ValidManifest);
        try
        {
            var discovered = AppActionManifestParser.Discover(root);

            Assert.That(discovered, Has.Count.EqualTo(1));
            Assert.That(discovered[0].Key, Is.EqualTo("hello"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void Discover_IgnoresDirectoriesWithoutAnActionManifest()
    {
        var root = Path.Combine(Path.GetTempPath(), "automaton-actions", Guid.NewGuid().ToString("N"));
        var action = Path.Combine(root, "actions", "hello");
        Directory.CreateDirectory(action);
        Directory.CreateDirectory(Path.Combine(root, "actions", "__pycache__"));
        Directory.CreateDirectory(Path.Combine(root, "actions", "supporting-script"));
        File.WriteAllText(Path.Combine(action, "action.yaml"), ValidManifest);
        try
        {
            var discovered = AppActionManifestParser.Discover(root);

            Assert.That(discovered.Select(x => x.Key), Is.EqualTo(new[] { "hello" }));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void ValidatesRequiredInputPropertiesAndTypes()
    {
        const string yaml = """
            apiVersion: automaton.dev/v1
            kind: Action
            metadata:
              key: hello
            spec:
              runtime: bash
              entrypoint: main.sh
              input:
                type: object
                properties:
                  name:
                    type: string
                required:
                  - name
            """;
        var manifest = AppActionManifestParser.Parse(yaml, "hello");
        using var missing = System.Text.Json.JsonDocument.Parse("{}");
        using var wrongType = System.Text.Json.JsonDocument.Parse("{\"name\":42}");
        using var valid = System.Text.Json.JsonDocument.Parse("{\"name\":\"Ada\"}");

        Assert.That(AppActionSchemaValidator.Validate(missing.RootElement, manifest.Spec.Input), Does.Contain("required"));
        Assert.That(AppActionSchemaValidator.Validate(wrongType.RootElement, manifest.Spec.Input), Does.Contain("string"));
        Assert.That(AppActionSchemaValidator.Validate(valid.RootElement, manifest.Spec.Input), Is.Null);
    }

    [TestCase("restart-hosted-runner")]
    [TestCase("delete-hosted-runner")]
    public void ParsesTrustedInfrastructureLifecycleAction(string actionKey)
    {
        var repositoryRoot = FindRepositoryRoot();
        var actionRoot = Path.Combine(repositoryRoot, "system-apps", "automaton-infrastructure", "actions", actionKey);

        var manifest = AppActionManifestParser.ParseFile(
            Path.Combine(actionRoot, "action.yaml"),
            actionKey,
            allowPrivilegedCapabilities: true);

        Assert.That(manifest.Spec.Capabilities, Does.Contain(AppActionCapabilities.Docker));
        Assert.That(manifest.Spec.Capabilities, Does.Contain(AppActionCapabilities.Host));
        Assert.That(File.Exists(Path.Combine(actionRoot, manifest.Spec.Entrypoint)), Is.True);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "system-apps"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Repository root was not found from the test output directory.");
    }
}
