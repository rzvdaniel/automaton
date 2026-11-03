// Opens a copy of an installation's Data Protection key ring exactly as the API does and decrypts every protected
// value in a PostgreSQL database, reporting counts only (never values). Used to prove that migrated secrets still decrypt.
//
//   dotnet run -- <key-ring-directory> "<postgresql connection string>"
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

if (args.Length != 2)
{
    Console.Error.WriteLine("usage: ProtectedDataCheck <key-ring-directory> <postgresql connection string>");
    return 2;
}

var services = new ServiceCollection();
services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(args[0])).SetApplicationName("Automaton.Api");
var provider = services.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>();
var secrets = provider.CreateProtector("Automaton.Secrets", "SecretVersion", "v1");
var operations = provider.CreateProtector("RunnerExecutionOperation.v1");

var checks = new (string Table, string Column, Microsoft.AspNetCore.DataProtection.IDataProtector Protector)[]
{
    ("SecretVersions", "ProtectedValue", secrets),
    ("RunnerExecutionOperations", "ProtectedRequest", operations),
    ("RunnerExecutionOperations", "ResultJson", operations),
};

var failed = 0;
await using var connection = new NpgsqlConnection(args[1]);
await connection.OpenAsync();
foreach (var (table, column, protector) in checks)
{
    int ok = 0, bad = 0;
    await using var command = new NpgsqlCommand($"SELECT \"{column}\" FROM \"{table}\" WHERE \"{column}\" IS NOT NULL", connection);
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        try { protector.Unprotect(reader.GetString(0)); ok++; }
        catch (Exception) { bad++; }
    }
    failed += bad;
    Console.WriteLine($"{table}.{column}: {ok} decrypted, {bad} failed");
}
return failed == 0 ? 0 : 1;
