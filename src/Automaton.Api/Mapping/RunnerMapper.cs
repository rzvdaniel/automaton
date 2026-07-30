using Automaton.Core.Enums;
using System.Collections.Generic;
using System.Linq;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Api.Entities;

namespace Automaton.Api.Mapping;

public static class RunnerMapper
{
 public static RunnerDetails ToRunnerDetails(
	 Runner runner,
	 RunnerStatus status = RunnerStatus.None,
	 Guid? projectId = null,
	 string? projectName = null,
     Guid? hostedRunnerId = null)
 {
	var scope = projectId.HasValue ? AccessScope.Project : AccessScope.Personal;

	 return new RunnerDetails
	 {
		 Id = runner.Id,
		 Name = runner.Name,
		 ConnectionId = runner.ConnectionId,
		 Status = status,
		 LastHeartbeatAtUtc = runner.LastHeartbeatAtUtc,
		 LastConnectedAtUtc = runner.LastConnectedAtUtc,
		 LastDisconnectedAtUtc = runner.LastDisconnectedAtUtc,
		 CurrentSessionConnectedAtUtc = null,
		 TotalConnectedDurationSeconds = 0,
		 ConnectionSessionCount = 0,
         CurrentSessionId = null,
         LastDisconnectReason = null,
		 Version = runner.Version,
		 MachineName = runner.MachineName,
		 IpAddress = runner.IpAddress,
         ApiInstanceId = runner.ApiInstanceId,
         ApiColor = runner.ApiColor,
         ImageDigest = runner.ImageDigest,
         ContainerSpecificationVersion = runner.ContainerSpecificationVersion,
         ApiCompatibilityVersion = runner.ApiCompatibilityVersion,
         OperatingSystem = runner.OperatingSystem,
         Architecture = runner.Architecture,
         ContainerHealth = runner.ContainerHealth,
         Purpose = RunnerPurposes.Normalize(runner.Purpose),
         Capabilities = RunnerCapabilities.Deserialize(runner.CapabilitiesJson, runner.Purpose),
		 Scope = scope,
		 ProjectId = projectId,
		 ProjectName = projectName,
         HostedRunnerId = hostedRunnerId,
         IsHostedRunner = hostedRunnerId.HasValue,
	 };
 }

 public static IEnumerable<RunnerDetails> ToRunnerDetailsList(
	 IEnumerable<Runner> runners,
	 RunnerStatus status = RunnerStatus.None,
	 IDictionary<Guid, (Guid ProjectId, string ProjectName)>? projectLookup = null,
     IDictionary<Guid, Guid>? hostedRunnerLookup = null)
 {
	 return runners.Select(r =>
	 {
		 if (projectLookup != null && projectLookup.TryGetValue(r.Id, out var link))
		 {
			 return ToRunnerDetails(
                 r,
                 status,
                 link.ProjectId,
                 link.ProjectName,
                 hostedRunnerLookup != null && hostedRunnerLookup.TryGetValue(r.Id, out var hostedRunnerId) ? hostedRunnerId : null);
		 }

         return ToRunnerDetails(
             r,
             status,
             null,
             null,
             hostedRunnerLookup != null && hostedRunnerLookup.TryGetValue(r.Id, out var fallbackHostedRunnerId) ? fallbackHostedRunnerId : null);
	 });
 }

 public static Runner ToRunner(RunnerDetails details)
 {
 return new Runner
 {
 Id = details.Id,
 Name = details.Name,
 ConnectionId = details.ConnectionId,
 LastHeartbeatAtUtc = details.LastHeartbeatAtUtc,
 LastConnectedAtUtc = details.LastConnectedAtUtc,
 LastDisconnectedAtUtc = details.LastDisconnectedAtUtc,
 Version = details.Version,
 MachineName = details.MachineName,
 IpAddress = details.IpAddress,
 ApiInstanceId = details.ApiInstanceId,
 ApiColor = details.ApiColor,
 ImageDigest = details.ImageDigest,
 ContainerSpecificationVersion = details.ContainerSpecificationVersion,
 ApiCompatibilityVersion = details.ApiCompatibilityVersion,
 OperatingSystem = details.OperatingSystem,
 Architecture = details.Architecture,
 ContainerHealth = details.ContainerHealth,
 Purpose = RunnerPurposes.Normalize(details.Purpose),
 CapabilitiesJson = RunnerCapabilities.Serialize(details.Capabilities, details.Purpose)
 // ScriptEngineSettings and RunnerUsers are not present in RunnerDetails
 };
 }

 public static IEnumerable<Runner> ToRunnerList(IEnumerable<RunnerDetails> detailsList)
 {
 return detailsList.Select(d => ToRunner(d));
 }
}
