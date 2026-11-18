namespace Automaton.Core.Models;

/// <summary>An app runtime a Runner hosts and the address where it listens on the Runner's host.</summary>
public sealed record LogSinkRuntimeReport(Guid AppId, Guid DeploymentTargetId, string Address);

/// <summary>The approved app that receives this server's logs, and where the Runner reaches it.</summary>
public sealed record LogSinkAssignment(Guid AppId, Guid DeploymentTargetId, string Address);
