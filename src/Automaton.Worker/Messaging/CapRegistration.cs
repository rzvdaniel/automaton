using Automaton.Messaging;
using DotNetCore.CAP;

namespace Automaton.Worker.Messaging;

internal static class CapRegistration
{
    internal static void ConfigureStorage(CapOptions options, string connectionString)
    {
        options.UsePostgreSql(postgresOptions =>
        {
            postgresOptions.ConnectionString = connectionString;
            postgresOptions.Schema = CapStorageTablePrefixes.Worker;
        });
    }
}
