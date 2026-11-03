using Automaton.Api.Controllers;
using Microsoft.AspNetCore.Authorization;
using NUnit.Framework;

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
}
