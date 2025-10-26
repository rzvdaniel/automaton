using Automaton.Studio.Server.Entities;
using Automaton.Studio.Server.Models;
using Automaton.Core.Enums;
using System.Collections.Generic;
using System.Linq;

namespace Automaton.Studio.Server.Mapping;

public static class RunnerMapper
{
 public static RunnerDetails ToRunnerDetails(Runner runner, RunnerStatus status = RunnerStatus.None)
 {
 return new RunnerDetails
 {
 Id = runner.Id,
 Name = runner.Name,
 ConnectionId = runner.ConnectionId,
 Status = status
 };
 }

 public static IEnumerable<RunnerDetails> ToRunnerDetailsList(IEnumerable<Runner> runners, RunnerStatus status = RunnerStatus.None)
 {
 return runners.Select(r => ToRunnerDetails(r, status));
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
