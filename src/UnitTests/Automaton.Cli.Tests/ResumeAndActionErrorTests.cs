using Automaton.Cli.Commands;
using Automaton.Cli.Services;
using NUnit.Framework;

namespace Automaton.Cli.Tests;

public sealed class ResumeAndActionErrorTests
{
    [Test]
    public void ApiErrorText_UsesTheProblemTitle()
        => Assert.That(
            ActionCommand.ApiErrorText("API error 409 Conflict: {\"title\":\"The app is paused.\",\"status\":409}"),
            Is.EqualTo("The app is paused."));

    [Test]
    public void ApiErrorText_KeepsPlainText()
        => Assert.That(ActionCommand.ApiErrorText("boom"), Is.EqualTo("boom"));

    [Test]
    public void RemovePausedNote_DropsTheStaysStoppedSentence()
    {
        var message = "Deployed. " + DeployService.PausedNoteStart + " Resume it with 'automaton resume'.";
        Assert.Multiple(() =>
        {
            Assert.That(DeployService.RemovePausedNote(message), Is.EqualTo("Deployed."));
            Assert.That(DeployService.RemovePausedNote(DeployService.PausedNoteStart + " x"), Is.Null);
            Assert.That(DeployService.RemovePausedNote("Deployed."), Is.EqualTo("Deployed."));
        });
    }
}
