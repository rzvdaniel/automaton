using Automaton.Api.Data;
using Automaton.Messaging;
using DotNetCore.CAP;

namespace Automaton.Api.Messaging;

internal static class CapRegistration
{
    internal static void ConfigureStorage(CapOptions options)
    {
        options.UseEntityFramework<ApplicationDbContext>(capOptions =>
            capOptions.TableNamePrefix = CapStorageTablePrefixes.Api);
    }
}
