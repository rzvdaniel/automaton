using System.Text.Json;
using Automaton.Core.Models;

namespace Automaton.Runner.Console.Services;

internal sealed class RunnerExecutionJournal(string directory)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public FileStream Lock(Guid id)
    {
        Directory.CreateDirectory(directory);
        return new FileStream(Path.Combine(directory, $"{id:N}.lock"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
    }
    public async Task<RunnerExecutionResult?> ReadAsync(Guid id, CancellationToken token)
    {
        if (!File.Exists(ResultPath(id))) return null;
        return JsonSerializer.Deserialize<RunnerExecutionResult>(await File.ReadAllTextAsync(ResultPath(id), token), JsonOptions)
            ?? throw new InvalidDataException("Saved Runner execution result is invalid.");
    }
    public bool WasAccepted(Guid id) => File.Exists(Path.Combine(directory, $"{id:N}.accepted"));
    public void Accept(Guid id)
    {
        using var file = new FileStream(Path.Combine(directory, $"{id:N}.accepted"), FileMode.CreateNew,
            FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
        file.WriteByte(1);
        file.Flush(true);
    }
    public async Task SaveAsync(Guid id, RunnerExecutionResult result, CancellationToken token)
    {
        var temporary = ResultPath(id) + ".tmp";
        await using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write,
            FileShare.None, 4096, FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(file, result, JsonOptions, token);
            file.Flush(true);
        }
        File.Move(temporary, ResultPath(id), overwrite: true);
    }
    private string ResultPath(Guid id) => Path.Combine(directory, $"{id:N}.json");
    public void Forget(Guid id)
    {
        // API terminal state is authoritative after its receipt. This avoids retaining private results indefinitely.
        foreach (var suffix in new[] { ".json", ".accepted", ".lock", ".json.tmp" })
        {
            try { File.Delete(Path.Combine(directory, $"{id:N}{suffix}")); }
            catch (IOException exception) { Serilog.Log.Warning(exception, "Execution journal cleanup deferred for {OperationId}.", id); }
            catch (UnauthorizedAccessException exception) { Serilog.Log.Warning(exception, "Execution journal cleanup deferred for {OperationId}.", id); }
        }
    }
}
