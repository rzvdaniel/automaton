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

    [TestCase("Jane", "Jane invited you to the project Demo")]
    [TestCase("", "You are invited to the project Demo")]
    public async Task ProjectInvitation_renders_inviter_expiry_and_link(string inviter, string expected)
    {
        Assert.That(new FluidParser().TryParse(Templates.GetString("AutomatonProjectInvitation.Body")!, out var t, out _));
        var ctx = new TemplateContext(new Dictionary<string, object?>
        {
            ["Name"] = "A", ["ProjectName"] = "Demo", ["InviterName"] = inviter,
            ["InviteUrl"] = "https://studio.test/invite/x", ["ExpirationHours"] = "24"
        });
        var html = await t.RenderAsync(ctx);

        Assert.That(html, Does.Contain(expected));
        Assert.That(html, Does.Contain("valid for 24 hours"));
        Assert.That(html, Does.Contain("https://studio.test/invite/x"));
    }
}
