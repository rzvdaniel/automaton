using Automaton.Api.Extensions;
using NUnit.Framework;
using Serilog;

namespace Automaton.Api.Tests;

[TestFixture]
public class DiagnosticLogRetentionTests
{
    [Test]
    public void DiagnosticLogsRollAtSizeAndRetainRecentFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"automaton-log-retention-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var configuration = new LoggerConfiguration();
            ApiLoggerSinks.Configure(configuration, false, directory, 3, 256);
            using (var logger = configuration.CreateLogger())
            {
                for (var index = 0; index < 12; index++)
                {
                    logger.Information("Retention event {Index}: {Content}", index, new string('x', 128));
                }
            }

            var files = Directory.GetFiles(directory, "diagnostic-*.log");
            Assert.That(files.Length, Is.InRange(2, 3));
            Assert.That(files.Any(file => File.ReadAllText(file).Contains("Retention event 11:", StringComparison.Ordinal)), Is.True);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
