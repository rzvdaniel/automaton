using System.Security.Claims;
using System.Text.Json;
using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;


namespace Automaton.Api.Tests;

[TestFixture]
public class AppActionRunnerAuthorizationTests
{


    [Test]
    public async Task RunnerVisibility_DoesNotLeakAcrossProjectContexts()
    {
        var userId = Guid.NewGuid();
        var projectAId = Guid.NewGuid();
        var projectBId = Guid.NewGuid();
        var runnerId = Guid.NewGuid();

        await using var dbContext = AppAuthorizationTestData.CreateDbContext();
        var visibleRunnerIds = await AppAuthorizationRunnerTestData.GetRunnerIdsVisibleToProjectAsync(
            dbContext, projectAId, projectBId, userId, runnerId);

        Assert.That(visibleRunnerIds, Is.Empty);
    }
}
