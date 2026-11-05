using System.Resources;
using Fluid;
using NUnit.Framework;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class EmailTemplateWordingTests
{
    private static readonly ResourceManager Templates = new(
        "Automaton.Infrastructure.Resources.EmailTemplates",
        typeof(Automaton.Api.Services.EmailService).Assembly);

    private static readonly string[] BodyKeys =
    {
        "AutomatonRegisterUser", "AutomatonConfirmEmail", "AutomatonProjectInvitation",
        "AutomatonOrganizationInvitation", "AutomatonResetPassword", "AutomatonResetPasswordConfirmation",
        "AutomatonUserInvitation", "AutomatonAdminNewRegistration"
    };

    [TestCaseSource(nameof(BodyKeys))]
    public void Body_parses_and_has_no_outdated_wording(string key)
    {
        var body = Templates.GetString(key + ".Body")!;

        Assert.That(new FluidParser().TryParse(body, out _, out var error), Is.True, error);
        Assert.That(body, Does.Not.Contain("Workflow automation made simple"));
        Assert.That(body, Does.Not.Contain("exciting project"));
        Assert.That(body, Does.Not.Contain("All rights reserved"));
    }
}
