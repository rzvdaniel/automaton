using System.IO.Compression;
using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
public sealed class RunnerPackageFileModeTests
{
    [Test]
    public void ExtractedFilesAreReadableByTheAppRuntimeUserWhateverModeTheAuthorUsed()
    {
        if (OperatingSystem.IsWindows()) Assert.Ignore("Unix file modes only.");
        var root = Path.Combine(Path.GetTempPath(), "pkg-mode-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var package = Path.Combine(root, "app.zip");
            using (var zip = ZipFile.Open(package, ZipArchiveMode.Create))
            {
                Add(zip, "index.html", 0b110_000_000);          // 0600, author's umask 077
                Add(zip, "bin/start.sh", 0b111_000_000);        // 0700, keeps its executable bit
                Add(zip, "private/data.json", 0b100_000_000);   // 0400
            }
            var staging = Path.Combine(root, "staging");
            Directory.CreateDirectory(staging);

            RunnerDeploymentWorkspace.ExtractPackageSafely(package, staging);

            var readable = UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead;
            var executable = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            foreach (var file in new[] { "index.html", "bin/start.sh", "private/data.json" })
                Assert.That(File.GetUnixFileMode(Path.Combine(staging, file)) & readable, Is.EqualTo(readable), file);
            Assert.That(File.GetUnixFileMode(Path.Combine(staging, "bin/start.sh")) & executable, Is.EqualTo(executable));
            Assert.That(File.GetUnixFileMode(Path.Combine(staging, "index.html")) & executable, Is.EqualTo(UnixFileMode.None));
            foreach (var directory in new[] { "bin", "private" })
                Assert.That(File.GetUnixFileMode(Path.Combine(staging, directory)) & (readable | executable), Is.EqualTo(readable | executable), directory);
        }
        finally { Directory.Delete(root, true); }
    }

    private static void Add(ZipArchive zip, string name, int mode)
    {
        var entry = zip.CreateEntry(name);
        entry.ExternalAttributes = mode << 16;
        using var writer = new StreamWriter(entry.Open());
        writer.Write("x");
    }
}
