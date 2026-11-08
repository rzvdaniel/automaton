using Automaton.Api.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using System.Security.Claims;

namespace Automaton.Api.Tests;

public class ProjectApiKeyAccessTests
{
    [TestCase(typeof(ProjectsController), nameof(ProjectsController.ListProjects), null)]
    [TestCase(typeof(ProjectsController), nameof(ProjectsController.GetProject), "ProjectMember")]
    [TestCase(typeof(ProjectsController), nameof(ProjectsController.CreateProject), null)]
    [TestCase(typeof(ProjectsController), nameof(ProjectsController.UpdateProject), "ProjectAdmin")]
    [TestCase(typeof(ProjectResourcesController), nameof(ProjectResourcesController.ListProjectApps), "ProjectMember")]
    [TestCase(typeof(ProjectResourcesController), nameof(ProjectResourcesController.AddAppToProject), "ProjectResourceManager")]
    [TestCase(typeof(ProjectResourcesController), nameof(ProjectResourcesController.RemoveAppFromProject), "ProjectResourceManager")]
    [TestCase(typeof(ProjectMembersController), nameof(ProjectMembersController.ListMembers), "ProjectMember")]
    public void AutomatableProjectEndpointsAcceptApiKeysAndKeepTheirProjectPolicy(Type controller, string action, string? projectPolicy)
    {
        var policies = controller.GetMethod(action)!.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>().Select(attribute => attribute.Policy).ToList();

        Assert.That(policies, Does.Contain("AllowApiKey"));
        if (projectPolicy != null) Assert.That(policies, Does.Contain(projectPolicy), "the project role check stays");
    }

    [TestCase(nameof(ProjectResourcesController.ListProjectRunners), "ProjectMember")]
    [TestCase(nameof(ProjectResourcesController.AddRunnerToProject), "ProjectResourceManager")]
    [TestCase(nameof(ProjectResourcesController.RemoveRunnerFromProject), "ProjectResourceManager")]
    public void ProjectRunnerActionsRequireDeveloperKeyAndProjectRole(string action, string projectPolicy)
    {
        var policies = typeof(ProjectResourcesController).GetMethod(action)!
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()
            .Select(attribute => attribute.Policy).ToList();
        Assert.That(policies, Does.Contain("DeveloperApiAccess"));
        Assert.That(policies, Does.Contain(projectPolicy));
    }

    [TestCase("ReadOnly")]
    [TestCase("Runner")]
    [TestCase("WebApp")]
    [TestCase("AppPermission")]
    public async Task OtherKeyScopesCannotChangeProjectRunners(string scope)
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()), new Claim("api_key_scope", scope)], "ApiKey"));
        var resources = new ProjectResourcesController(null!, NullLogger<ProjectResourcesController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } }
        };
        var added = (await resources.AddRunnerToProject(Guid.NewGuid(), Guid.NewGuid())).Result;
        var removed = await resources.RemoveRunnerFromProject(Guid.NewGuid(), Guid.NewGuid());
        Assert.That((added as ObjectResult)?.StatusCode, Is.EqualTo(StatusCodes.Status403Forbidden));
        Assert.That((removed as ObjectResult)?.StatusCode, Is.EqualTo(StatusCodes.Status403Forbidden));
    }

    [Test]
    public void MemberChangesAndProjectDeletionStayStudioOnly()
    {
        foreach (var method in new[]
        {
            typeof(ProjectsController).GetMethod(nameof(ProjectsController.DeleteProject))!,
            typeof(ProjectMembersController).GetMethod(nameof(ProjectMembersController.AddMember))!,
            typeof(ProjectMembersController).GetMethod(nameof(ProjectMembersController.RemoveMember))!,
        })
        {
            Assert.That(method.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()
                .Select(attribute => attribute.Policy), Does.Not.Contain("AllowApiKey"), method.Name);
        }
    }

    [Test]
    public async Task ReadOnlyKeysCannotChangeProjects()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()), new Claim("api_key_scope", "ReadOnly")], "ApiKey"));
        var context = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } };
        var projects = new ProjectsController(null!, NullLogger<ProjectsController>.Instance) { ControllerContext = context };
        var resources = new ProjectResourcesController(null!, NullLogger<ProjectResourcesController>.Instance) { ControllerContext = context };

        var results = new[]
        {
            (await projects.CreateProject(new())).Result,
            (await projects.UpdateProject(Guid.NewGuid(), new())).Result,
            (await resources.AddAppToProject(Guid.NewGuid(), Guid.NewGuid())).Result,
            await resources.RemoveAppFromProject(Guid.NewGuid(), Guid.NewGuid()) as IActionResult,
            (await resources.AddRunnerToProject(Guid.NewGuid(), Guid.NewGuid())).Result,
            await resources.RemoveRunnerFromProject(Guid.NewGuid(), Guid.NewGuid()) as IActionResult,
        };

        Assert.That(results.Select(result => (result as ObjectResult)?.StatusCode), Has.All.EqualTo(StatusCodes.Status403Forbidden));
    }
}
