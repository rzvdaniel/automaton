using System.Reflection;
namespace Automaton.AgentOrchestrator.Hosting;

public sealed record BuildIdentity(string Version, string Revision);

public interface IBuildIdentity
{
    BuildIdentity Current { get; }
}

public sealed class AssemblyBuildIdentity : IBuildIdentity
{
    public AssemblyBuildIdentity() : this(typeof(AssemblyBuildIdentity).Assembly) { }

    internal AssemblyBuildIdentity(Assembly assembly)
    {
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        Current = Parse(informational);
    }

    internal static BuildIdentity Parse(string informational)
    {
        var parts = informational.Split('+', 2);
        return new(parts[0], parts.Length == 2 && parts[1].Length > 0 ? parts[1] : "unknown");
    }

    public BuildIdentity Current { get; }
}
