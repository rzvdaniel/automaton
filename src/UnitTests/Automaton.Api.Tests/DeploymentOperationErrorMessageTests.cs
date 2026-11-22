using Automaton.Api.Entities;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class DeploymentOperationErrorMessageTests
{
    [Test]
    public void ShortError_IsKeptAsIs()
        => Assert.That(DeploymentOperation.FitErrorMessage("boom"), Is.EqualTo("boom"));

    [Test]
    public void Null_StaysNull()
        => Assert.That(DeploymentOperation.FitErrorMessage(null), Is.Null);

    [Test]
    public void LongError_FitsTheColumnAndKeepsStartAndEnd()
    {
        var error = "START-" + new string('x', 10000) + "-END";
        var fitted = DeploymentOperation.FitErrorMessage(error)!;
        Assert.That(fitted.Length, Is.LessThanOrEqualTo(DeploymentOperation.ErrorMessageMaxLength));
        Assert.That(fitted, Does.StartWith("START-"));
        Assert.That(fitted, Does.EndWith("-END"));
        Assert.That(fitted, Does.Contain("cut to fit"));
    }

    [Test]
    public void LongError_DoesNotSplitAnEmoji()
    {
        var fitted = DeploymentOperation.FitErrorMessage(string.Concat(Enumerable.Repeat("😀", 5000)))!;
        Assert.That(fitted.Length, Is.LessThanOrEqualTo(DeploymentOperation.ErrorMessageMaxLength));
        Assert.That(() => new System.Text.UTF8Encoding(false, true).GetBytes(fitted), Throws.Nothing);
    }
}
