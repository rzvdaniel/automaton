namespace Automaton.Core.Models;

/// <summary>An app runtime a Runner hosts and the address where it listens on the Runner's host.</summary>
public sealed record LogSinkRuntimeReport(Guid AppId, Guid DeploymentTargetId, string Address);

/// <summary>A runtime its Runner could not start (<paramref name="Failed"/>), or one that came back after failing.</summary>
public sealed record RuntimeStateReport(Guid AppId, Guid DeploymentTargetId, bool Failed, string? Error);

/// <summary>The approved app that receives this server's logs, and where the Runner reaches it.</summary>
public sealed record LogSinkAssignment(Guid AppId, Guid DeploymentTargetId, string Address);
