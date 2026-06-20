using Automaton.Api.Controllers;
using Automaton.Api.Middleware;

namespace Automaton.Api.Tests;

[TestFixture]
public class AppMediaControllerTests
{
    [Test]
    public void Upload_UsesDynamicMediaUploadRequestLimitsFilter()
    {
        var method = typeof(AppMediaController).GetMethod(nameof(AppMediaController.Upload));

        Assert.That(method, Is.Not.Null);
        Assert.That(
            method!.GetCustomAttributes(typeof(MediaUploadRequestLimitsAttribute), inherit: true),
            Has.Length.EqualTo(1));
    }
}
