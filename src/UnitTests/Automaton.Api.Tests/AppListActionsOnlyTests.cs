using Automaton.Api.Services;
using Automaton.Core.Models;

namespace Automaton.Api.Tests;

public sealed class AppListActionsOnlyTests
{
    [Test]
    public void AppWithoutRuntimeCommandOrStaticSite_IsActionsOnly()
        => Assert.That(AppListingService.IsActionsOnly(ApplicationContractSerialization.Serialize(new ApplicationContract())), Is.True);

    [Test]
    public void AppWithRuntimeCommand_IsNotActionsOnly()
    {
        var contract = new ApplicationContract
        {
            Runtime = new ApplicationRuntimeSpec { Command = new ApplicationCommandSpec { Executable = "node" } }
        };
        Assert.That(AppListingService.IsActionsOnly(ApplicationContractSerialization.Serialize(contract)), Is.False);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("not json")]
    public void MissingOrUnreadableContract_IsNotActionsOnly(string? json)
        => Assert.That(AppListingService.IsActionsOnly(json), Is.False);

    [Test]
    public void AppDetail_CarriesActionsOnlyFromTheContract()
    {
        var actionsOnly = new Automaton.Api.Entities.WebApp { Definition = "{}", ApplicationContractJson = ApplicationContractSerialization.Serialize(new ApplicationContract()) };
        var withRuntime = new Automaton.Api.Entities.WebApp
        {
            Definition = "{}",
            ApplicationContractJson = ApplicationContractSerialization.Serialize(new ApplicationContract
            {
                Runtime = new ApplicationRuntimeSpec { Command = new ApplicationCommandSpec { Executable = "node" } }
            })
        };
        Assert.That(AppsService.MapToModel(actionsOnly).ActionsOnly, Is.True);
        Assert.That(AppsService.MapToModel(withRuntime).ActionsOnly, Is.False);
    }
}
