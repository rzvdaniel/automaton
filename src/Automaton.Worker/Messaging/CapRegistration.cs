using Automaton.Messaging;
using DotNetCore.CAP;

namespace Automaton.Worker.Messaging;

internal static class CapRegistration
{
    internal static void ConfigureStorage(CapOptions options, string connectionString)
    {
        options.UseMySql(mysqlOptions =>
        {
            mysqlOptions.ConnectionString = connectionString;
            mysqlOptions.TableNamePrefix = CapStorageTablePrefixes.Worker;
        });
    }
}
