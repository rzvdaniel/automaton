using Automaton.Studio.Server.Entities;
using Automaton.Studio.Server.Models;
using Automaton.Core.Enums;
using Automaton.Studio.Server.Enums;
using System.Collections.Generic;
using System.Linq;

namespace Automaton.Studio.Server.Mapping;

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
 ConnectionId = details.ConnectionId
 // ScriptEngineSettings and RunnerUsers are not present in RunnerDetails
 };
 }

 public static IEnumerable<Runner> ToRunnerList(IEnumerable<RunnerDetails> detailsList)
 {
 return detailsList.Select(d => ToRunner(d));
 }
}
