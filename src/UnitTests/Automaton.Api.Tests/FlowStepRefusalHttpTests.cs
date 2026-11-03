using System.Net;
using System.Net.Http.Json;
using Automaton.Api.Entities;
using Microsoft.AspNetCore.TestHost;
using NUnit.Framework;

namespace Automaton.Api.Tests;

/// <summary>A refused flow or custom-step request answers 403 or 400 through the real pipeline, never a generic 500.</summary>
[TestFixture]
public sealed class FlowStepRefusalHttpTests
{
    private static async Task<HttpResponseMessage> SendAsync(bool owner, HttpMethod method, Func<Guid, string> url, Func<Guid, object?> body, bool seedStep = false)
    {
        var ownerId = Guid.NewGuid();
        var userId = owner ? ownerId : Guid.NewGuid();
        var resource = Guid.NewGuid();
        var project = Guid.NewGuid();
        await using var db = AppAuthorizationTestData.CreateDbContext();
        db.Projects.Add(AppAuthorizationTestData.CreateProject(project, ownerId, "project"));
        if (seedStep)
        {
            db.CustomStepUsers.Add(new CustomStepUser { CustomStepId = resource, UserId = ownerId });
            db.ProjectCustomSteps.Add(new ProjectCustomStep { ProjectId = project, CustomStepId = resource });
        }
        else
        {
            db.FlowUsers.Add(new FlowUser { FlowId = resource, UserId = ownerId });
            db.ProjectFlows.Add(new ProjectFlow { ProjectId = project, FlowId = resource });
        }
        await db.SaveChangesAsync();
        using var host = await FlowStepHttpTestHost.CreateAsync(db, userId);
        using var request = new HttpRequestMessage(method, url(resource));
        if (body(resource) is { } payload) request.Content = JsonContent.Create(payload);
        return await host.GetTestClient().SendAsync(request);
    }

    [Test]
    public async Task UpdatingSomeoneElsesFlow_Answers403()
    {
        using var response = await SendAsync(false, HttpMethod.Put, id => $"/api/flows/{id}", _ => new { name = "x" });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task RunningSomeoneElsesFlow_Answers403()
    {
        using var response = await SendAsync(false, HttpMethod.Post, _ => "/api/flows/run", id => new { flowId = id });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task UpdatingSomeoneElsesCustomStep_Answers403()
    {
        using var response = await SendAsync(false, HttpMethod.Put, id => $"/api/customsteps/{id}", _ => new { name = "x", visibleInExplorer = false }, seedStep: true);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task ReadingTheSettingsOfAFlowThatDoesNotExist_Answers404()
    {
        using var response = await SendAsync(false, HttpMethod.Get, _ => $"/api/flows/settings/{Guid.NewGuid()}", _ => null);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [TestCase(false, HttpStatusCode.Forbidden)]
    [TestCase(true, HttpStatusCode.BadRequest)]
    public async Task DeletingAProjectFlow_Answers403ForANonOwnerAnd400WithTheReasonForTheOwner(bool owner, HttpStatusCode expected)
    {
        using var response = await SendAsync(owner, HttpMethod.Delete, id => $"/api/flows/{id}", _ => null);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(expected), text);
            Assert.That(text.Contains("part of one or more projects"), Is.EqualTo(owner), text);
        });
    }

    [TestCase(false, HttpStatusCode.Forbidden)]
    [TestCase(true, HttpStatusCode.BadRequest)]
    public async Task DeletingAProjectCustomStep_Answers403ForANonOwnerAnd400WithTheReasonForTheOwner(bool owner, HttpStatusCode expected)
    {
        using var response = await SendAsync(owner, HttpMethod.Delete, id => $"/api/customsteps/{id}", _ => null, seedStep: true);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(expected));
            Assert.That(text.Contains("part of one or more projects"), Is.EqualTo(owner));
        });
    }
}
