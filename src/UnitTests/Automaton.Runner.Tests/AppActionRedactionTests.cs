using Automaton.Core.Models;
using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

public sealed class AppActionRedactionTests
{
    [Test]
    public async Task SecretsAreRedactedButPlainSettingsAreNot()
    {
        var request = new ExecuteAppActionModuleRequest
        {
            Secrets = new() { ["S3_SECRET_ACCESS_KEY"] = "shh-123", ["S3_REGION"] = "us-east-1" },
            PlainValueNames = ["S3_REGION"],
        };
        var path = Path.GetTempFileName();
        try
        {
            await AppActionProcessExecutor.WriteRedactedLogsAsync(request, path, "key shh-123 in us-east-1");
            Assert.That(await File.ReadAllTextAsync(path), Is.EqualTo("key [REDACTED] in us-east-1"));
        }
        finally { File.Delete(path); }
    }
}
