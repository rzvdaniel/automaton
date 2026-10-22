using System.Security.Claims;
using System.Data.Common;
using System.Text.Json;
using Automaton.Api.Config;
using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Models;
using Automaton.Core.Enums;
using Automaton.Core.Constants;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Events;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;



namespace Automaton.Api.Tests;

[TestFixture]
public class HostedRunnerRegistrationAuditTests
{
    [Test]
    public async Task RegistrationDoesNotCompleteRetiredStorageResume()
    {
        using var db = HostedRunnerAuditTestData.CreateDbContext();
        var userId = Guid.NewGuid();
        var (seed, resumeId) = await HostedRunnerRegistrationTestScenario.SeedRetiredResumeAsync(db, userId);

        await HostedRunnerProfileTestServiceFactory.CreateWithAudit(db, userId).Add(new RunnerDetails
        {
            Id = seed.RunnerId, Name = "new-display-name", Purpose = RunnerPurposes.Managed,
            HostedRunnerId = seed.HostedRunnerId
        });

        await HostedRunnerRegistrationTestScenario.AssertRetiredResumeIsUnchangedAsync(db, resumeId);
    }


    [Test]
    public async Task RunnerRegistration_CorrelatesByHostedRunnerIdAndCompletesReprovision()
    {
        using var db = HostedRunnerAuditTestData.CreateDbContext();
        var userId = Guid.NewGuid();
        var seed = HostedRunnerRegistrationTestScenario.Seed(db, userId);

        var runnerService = HostedRunnerProfileTestServiceFactory.CreateWithAudit(db, userId);

        await runnerService.Add(new RunnerDetails
        {
            Id = seed.RunnerId,
            Name = "new-display-name",
            Purpose = Automaton.Core.Enums.RunnerPurposes.Managed,
            HostedRunnerId = seed.HostedRunnerId
        });
        await HostedRunnerRegistrationTestScenario.AssertReprovisionCompletedAsync(db, seed);
    }
}
