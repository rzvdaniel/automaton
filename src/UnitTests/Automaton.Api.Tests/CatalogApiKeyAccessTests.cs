using Automaton.Api.Controllers;
using Microsoft.AspNetCore.Authorization;
using NUnit.Framework;

namespace Automaton.Api.Tests;

public class CatalogApiKeyAccessTests
{
    [Test]
    public void CatalogEndpointsAcceptDevelopmentApiKeysAndStayOnTheDeveloperPolicy()
    {
        var policies = typeof(CatalogController).GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>().Select(attribute => attribute.Policy).ToList();

        Assert.That(policies, Is.EqualTo(new[] { "DeveloperApiAccess" }), "list, install-source and install: a Developer key or a login, not read-only, runner or web-app keys");
    }

    [TestCase(nameof(CatalogController.Get))]
    [TestCase(nameof(CatalogController.InstallSource))]
    [TestCase(nameof(CatalogController.Install))]
    public void NoCatalogActionLoosensTheClassPolicy(string action)
    {
        var overrides = typeof(CatalogController).GetMethod(action)!.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>();
        Assert.That(overrides, Is.Empty);
    }
}
