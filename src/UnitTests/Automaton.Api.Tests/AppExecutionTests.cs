using Automaton.Api.Models;
using Automaton.Core.Enums;
using Automaton.Core.Models;

namespace Automaton.Api.Tests;

public class AppExecutionTests
{
    [Test]
    public void ApplyInputVariables_SetsValuesAndReturnsMissing()
    {
        // Arrange
        var flow = new Flow
        {
            InputVariables = new Dictionary<string, StepVariable>()
        };
        flow.InputVariables["Name"] = new StepVariable { Name = "Name", Value = null };
        flow.InputVariables["Count"] = new StepVariable { Name = "Count", Value = null };

        var inputs = new Dictionary<string, string>
        {
            { "Name", "John" },
            { "Age", "30" }
        };

        // Act
        var missing = AppExecutionTestsHelper.ApplyInputVariables(flow, inputs);

        // Assert
        Assert.That(missing, Does.Contain("Age"));
        Assert.That(flow.InputVariables["Name"].Value, Is.EqualTo("John"));
        Assert.That(flow.InputVariables["Count"].Value, Is.Null);
    }

    [Test]
    public void SelectRunners_RespectsRequestedNames()
    {
        var runners = new List<RunnerDetails>
        {
            new() { Id = Guid.NewGuid(), Name = "A", Status = RunnerStatus.Online },
            new() { Id = Guid.NewGuid(), Name = "B", Status = RunnerStatus.Online },
            new() { Id = Guid.NewGuid(), Name = "C", Status = RunnerStatus.Offline }
        };

        var selected = AppExecutionTestsHelper.SelectRunners(runners, new List<string> { "B" });

        Assert.That(selected.Count, Is.EqualTo(1));
        Assert.That(selected[0], Is.EqualTo(runners[1].Id));
    }

    [Test]
    public void SelectRunners_UsesExecutionRunnerWhenNoRequested()
    {
        var execId = Guid.NewGuid();
        var runners = new List<RunnerDetails>
        {
            new() { Id = execId, Name = "Exec", Status = RunnerStatus.Online },
            new() { Id = Guid.NewGuid(), Name = "Other", Status = RunnerStatus.Online }
        };

        var selected = AppExecutionTestsHelper.SelectRunners(runners, null, execId);

        Assert.That(selected.Count, Is.EqualTo(1));
        Assert.That(selected[0], Is.EqualTo(execId));
    }

    [Test]
    public void SelectRunners_ThrowsWhenNoRunnerSpecified()
    {
        var runners = new List<RunnerDetails>
        {
            new RunnerDetails { Id = Guid.NewGuid(), Name = "A", Status = RunnerStatus.Online }
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            AppExecutionTestsHelper.SelectRunners(runners, null, null)
        );

        Assert.That(ex.Message, Does.Contain("No runner specified"));
    }
}
