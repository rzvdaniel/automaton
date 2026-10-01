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
    public async Task<FileStream> LockAsync(Guid id, CancellationToken token)
    {
        while (true)
        {
            try { return Lock(id); }
            catch (IOException) { await Task.Delay(TimeSpan.FromMilliseconds(50), token); }
        }
    }
    public async Task<RunnerExecutionResult?> ReadAsync(Guid id, CancellationToken token)
    {
        if (!File.Exists(ResultPath(id))) return null;
        return JsonSerializer.Deserialize<RunnerExecutionResult>(await File.ReadAllTextAsync(ResultPath(id), token), JsonOptions)
            ?? throw new InvalidDataException("Saved Runner execution result is invalid.");
    }
    public bool WasAccepted(Guid id) => File.Exists(Path.Combine(directory, $"{id:N}.accepted"));
    public bool WasCancelled(Guid id) => File.Exists(Path.Combine(directory, $"{id:N}.cancelled"));
    public void CancelBeforeExecution(Guid id) => WriteMarker(id, ".cancelled");
    public void Accept(Guid id)
        => WriteMarker(id, ".accepted");
    private void WriteMarker(Guid id, string suffix)
    {
        using var file = new FileStream(Path.Combine(directory, $"{id:N}{suffix}"), FileMode.CreateNew,
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
        try
        {
            using var ownership = Lock(id);
            RemovePrivateResults(id);
        }
        catch (IOException exception) { Serilog.Log.Warning(exception, "Execution journal cleanup deferred for {OperationId}.", id); }
        catch (UnauthorizedAccessException exception) { Serilog.Log.Warning(exception, "Execution journal cleanup deferred for {OperationId}.", id); }
    }
    private void RemovePrivateResults(Guid id)
    {
        // Remove private results after API confirmation; retain acceptance/cancellation tombstones and lock inodes.
        // A concurrent delivery may already have fetched its assignment before the API became terminal.
        foreach (var suffix in new[] { ".json", ".json.tmp" })
        {
            try { File.Delete(Path.Combine(directory, $"{id:N}{suffix}")); }
            catch (IOException exception) { Serilog.Log.Warning(exception, "Execution journal cleanup deferred for {OperationId}.", id); }
            catch (UnauthorizedAccessException exception) { Serilog.Log.Warning(exception, "Execution journal cleanup deferred for {OperationId}.", id); }
        }
    }
}
