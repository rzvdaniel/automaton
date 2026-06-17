

using Automaton.Api.Models;

namespace Automaton.Api.Tests;

public class AppStepExecutionTests
{
    [Test]
    public void WebAppStepExecutionRequest_DefaultsToEmptyInputs()
    {
        // Arrange & Act
        var request = new AppStepExecutionRequest();

        // Assert
        Assert.That(request.Inputs, Is.Null);
        Assert.That(request.RunnerNames, Is.Null);
    }

    [Test]
    public void WebAppStepExecutionRequest_AcceptsInputs()
    {
        // Arrange
        var inputs = new Dictionary<string, string>
        {
            { "param1", "value1" },
            { "param2", "value2" }
        };

        // Act
        var request = new AppStepExecutionRequest
        {
            Inputs = inputs,
            RunnerNames = new List<string> { "runner1" }
        };

        // Assert
        Assert.That(request.Inputs, Is.Not.Null);
        Assert.That(request.Inputs.Count, Is.EqualTo(2));
        Assert.That(request.Inputs["param1"], Is.EqualTo("value1"));
        Assert.That(request.RunnerNames, Has.Count.EqualTo(1));
    }

    [Test]
    public void WebAppStepExecutionResponse_SuccessResponse()
    {
        // Arrange
        var stepId = Guid.NewGuid();
        var outputVars = new Dictionary<string, string>
        {
            { "result", "success" }
        };

        // Act
        var response = new AppStepExecutionResponse
        {
            Success = true,
            StepId = stepId,
            StepName = "TestStep",
            Status = "Completed",
            RunnerCount = 1,
            ExecutedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow.AddSeconds(1),
            OutputVariables = outputVars,
            Logs = new List<string> { "Step executed successfully" }
        };

        // Assert
        Assert.That(response.Success, Is.True);
        Assert.That(response.StepId, Is.EqualTo(stepId));
        Assert.That(response.StepName, Is.EqualTo("TestStep"));
        Assert.That(response.Status, Is.EqualTo("Completed"));
        Assert.That(response.RunnerCount, Is.EqualTo(1));
        Assert.That(response.OutputVariables, Is.Not.Null);
        Assert.That(response.OutputVariables["result"], Is.EqualTo("success"));
        Assert.That(response.Logs, Has.Count.EqualTo(1));
        Assert.That(response.Error, Is.Null);
    }

    [Test]
    public void WebAppStepExecutionResponse_FailureResponse()
    {
        // Arrange & Act
        var response = new AppStepExecutionResponse
        {
            Success = false,
            Status = "Failed",
            ExecutedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow.AddSeconds(1),
            Error = "Step execution failed",
            Logs = new List<string> { "Error occurred" }
        };

        // Assert
        Assert.That(response.Success, Is.False);
        Assert.That(response.Status, Is.EqualTo("Failed"));
        Assert.That(response.Error, Is.EqualTo("Step execution failed"));
        Assert.That(response.Logs, Has.Count.EqualTo(1));
    }

    [Test]
    public void AppStepInfo_BasicProperties()
    {
        // Arrange
        var stepId = Guid.NewGuid();

        // Act
        var stepInfo = new AppStepInfo
        {
            Id = stepId,
            Name = "SendEmailStep",
            DisplayName = "Send Email",
            Description = "Sends an email using SMTP",
            Language = "Python"
        };

        // Assert
        Assert.That(stepInfo.Id, Is.EqualTo(stepId));
        Assert.That(stepInfo.Name, Is.EqualTo("SendEmailStep"));
        Assert.That(stepInfo.DisplayName, Is.EqualTo("Send Email"));
        Assert.That(stepInfo.Description, Is.EqualTo("Sends an email using SMTP"));
        Assert.That(stepInfo.Language, Is.EqualTo("Python"));
    }

    [Test]
    public void WebAppStepExecutionResponse_DefaultStatus()
    {
        // Arrange & Act
        var response = new AppStepExecutionResponse();

        // Assert
        Assert.That(response.Status, Is.EqualTo("Unknown"));
    }
}
