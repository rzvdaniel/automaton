using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Automaton.Api.Constants;
using Automaton.Api.Application.Commands;
using Automaton.Api.Security;
using Automaton.Api.Middleware;

namespace Automaton.Studio.Server.Tests;

[TestFixture]
public class RateLimitingPipelineTests
{
    [Test]
    public async Task GlobalAnonymousRateLimiter_Returns429AfterConfiguredLimit()
    {
        using var host = await CreateHostAsync(configureOptions: options =>
        {
            options.GlobalAnonymous = new RateLimitPolicyOptions(3, 60);
        });
        var client = host.GetTestClient();

        HttpResponseMessage? blockedResponse = null;
        for (var i = 0; i < 4; i++)
        {
            blockedResponse = await client.GetAsync("/test-auth/global-anonymous");
        }

        Assert.That(blockedResponse, Is.Not.Null);
        Assert.That(blockedResponse!.StatusCode, Is.EqualTo((HttpStatusCode)429));
        Assert.That(blockedResponse.Headers.TryGetValues("Retry-After", out _), Is.True);
        await AssertGenericTooManyRequestsResponse(blockedResponse);
    }

    [Test]
    public async Task GlobalAuthenticatedRateLimiter_Returns429AfterConfiguredLimitPerUser()
    {
        using var host = await CreateHostAsync(configureOptions: options =>
        {
            options.GlobalAuthenticated = new RateLimitPolicyOptions(3, 60);
        });
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "user-1");

        HttpResponseMessage? blockedResponse = null;
        for (var i = 0; i < 4; i++)
        {
            blockedResponse = await client.GetAsync("/test-auth/global-authenticated");
        }

        Assert.That(blockedResponse, Is.Not.Null);
        Assert.That(blockedResponse!.StatusCode, Is.EqualTo((HttpStatusCode)429));
        Assert.That(blockedResponse.Headers.TryGetValues("Retry-After", out _), Is.True);
        await AssertGenericTooManyRequestsResponse(blockedResponse);
    }

    [Test]
    public async Task GlobalAuthenticatedRateLimiter_UsesSeparateBucketsPerUser()
    {
        using var host = await CreateHostAsync(configureOptions: options =>
        {
            options.GlobalAuthenticated = new RateLimitPolicyOptions(2, 60);
        });

        var firstClient = host.GetTestClient();
        firstClient.DefaultRequestHeaders.Add("X-Test-User", "user-1");

        var secondClient = host.GetTestClient();
        secondClient.DefaultRequestHeaders.Add("X-Test-User", "user-2");

        var firstResponse = await firstClient.GetAsync("/test-auth/global-authenticated");
        var secondResponse = await secondClient.GetAsync("/test-auth/global-authenticated");

        Assert.That(firstResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(secondResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task LoginRateLimiter_Returns429AfterFifthRequest()
    {
        using var host = await CreateHostAsync();
        var client = host.GetTestClient();

        HttpResponseMessage? blockedResponse = null;
        for (var i = 0; i < 6; i++)
        {
            blockedResponse = await client.PostAsJsonAsync("/test-auth/login", new
            {
                email = "user@example.com",
                password = "pw"
            });
        }

        Assert.That(blockedResponse, Is.Not.Null);
        Assert.That(blockedResponse!.StatusCode, Is.EqualTo((HttpStatusCode)429));
        Assert.That(blockedResponse.Headers.TryGetValues("Retry-After", out _), Is.True);
        await AssertGenericTooManyRequestsResponse(blockedResponse);
    }

    [Test]
    public async Task ForgotPasswordEmailThrottle_Returns429AfterThirdRequestForSameEmail()
    {
        using var host = await CreateHostAsync();
        var client = host.GetTestClient();

        HttpResponseMessage? blockedResponse = null;
        for (var i = 0; i < 4; i++)
        {
            blockedResponse = await client.PostAsJsonAsync("/test-auth/forgot-password", new
            {
                email = "user@example.com"
            });
        }

        Assert.That(blockedResponse, Is.Not.Null);
        Assert.That(blockedResponse!.StatusCode, Is.EqualTo((HttpStatusCode)429));
        Assert.That(blockedResponse.Headers.TryGetValues("Retry-After", out _), Is.True);
        await AssertGenericTooManyRequestsResponse(blockedResponse);
    }

    [Test]
    public async Task ResetPasswordRateLimiter_Returns429AfterFifthRequest()
    {
        using var host = await CreateHostAsync();
        var client = host.GetTestClient();

        HttpResponseMessage? blockedResponse = null;
        for (var i = 0; i < 6; i++)
        {
            blockedResponse = await client.PostAsJsonAsync("/test-auth/reset-password", new
            {
                email = "user@example.com",
                password = "Password123!",
                code = "reset-code"
            });
        }

        Assert.That(blockedResponse, Is.Not.Null);
        Assert.That(blockedResponse!.StatusCode, Is.EqualTo((HttpStatusCode)429));
        Assert.That(blockedResponse.Headers.TryGetValues("Retry-After", out _), Is.True);
        await AssertGenericTooManyRequestsResponse(blockedResponse);
    }

    [Test]
    public async Task RefreshTokenRateLimiter_Returns429AfterThirtiethRequest()
    {
        using var host = await CreateHostAsync();
        var client = host.GetTestClient();

        HttpResponseMessage? blockedResponse = null;
        for (var i = 0; i < 31; i++)
        {
            blockedResponse = await client.PostAsJsonAsync("/test-auth/refresh-token", new
            {
                token = "refresh-token"
            });
        }

        Assert.That(blockedResponse, Is.Not.Null);
        Assert.That(blockedResponse!.StatusCode, Is.EqualTo((HttpStatusCode)429));
        Assert.That(blockedResponse.Headers.TryGetValues("Retry-After", out _), Is.True);
        await AssertGenericTooManyRequestsResponse(blockedResponse);
    }

    [Test]
    public async Task ApiKeyValidationRateLimiter_Returns429AfterTwentiethRequest()
    {
        using var host = await CreateHostAsync();
        var client = host.GetTestClient();

        HttpResponseMessage? blockedResponse = null;
        for (var i = 0; i < 21; i++)
        {
            blockedResponse = await client.PostAsJsonAsync("/test-auth/apikey-validate", new
            {
                apiKey = "ak_runner_test"
            });
        }

        Assert.That(blockedResponse, Is.Not.Null);
        Assert.That(blockedResponse!.StatusCode, Is.EqualTo((HttpStatusCode)429));
        Assert.That(blockedResponse.Headers.TryGetValues("Retry-After", out _), Is.True);
        await AssertGenericTooManyRequestsResponse(blockedResponse);
    }

    [Test]
    public async Task PublicAppControllerExecutionRateLimiter_Returns429AfterTenthRequest()
    {
        using var host = await CreateHostAsync();
        var client = host.GetTestClient();

        HttpResponseMessage? blockedResponse = null;
        for (var i = 0; i < 11; i++)
        {
            blockedResponse = await client.PostAsJsonAsync("/test-auth/public/controller-execute", new
            {
                value = "test"
            });
        }

        Assert.That(blockedResponse, Is.Not.Null);
        Assert.That(blockedResponse!.StatusCode, Is.EqualTo((HttpStatusCode)429));
        Assert.That(blockedResponse.Headers.TryGetValues("Retry-After", out _), Is.True);
        await AssertGenericTooManyRequestsResponse(blockedResponse);
    }

    [Test]
    public async Task PublicAppFlowExecutionRateLimiter_Returns429AfterTenthRequest()
    {
        using var host = await CreateHostAsync();
        var client = host.GetTestClient();

        HttpResponseMessage? blockedResponse = null;
        for (var i = 0; i < 11; i++)
        {
            blockedResponse = await client.PostAsJsonAsync("/test-auth/public/flow-execute", new
            {
                value = "test"
            });
        }

        Assert.That(blockedResponse, Is.Not.Null);
        Assert.That(blockedResponse!.StatusCode, Is.EqualTo((HttpStatusCode)429));
        Assert.That(blockedResponse.Headers.TryGetValues("Retry-After", out _), Is.True);
        await AssertGenericTooManyRequestsResponse(blockedResponse);
    }

    [Test]
    public async Task PublicAppStepExecutionRateLimiter_Returns429AfterTenthRequest()
    {
        using var host = await CreateHostAsync();
        var client = host.GetTestClient();

        HttpResponseMessage? blockedResponse = null;
        for (var i = 0; i < 11; i++)
        {
            blockedResponse = await client.PostAsJsonAsync("/test-auth/public/step-execute", new
            {
                value = "test"
            });
        }

        Assert.That(blockedResponse, Is.Not.Null);
        Assert.That(blockedResponse!.StatusCode, Is.EqualTo((HttpStatusCode)429));
        Assert.That(blockedResponse.Headers.TryGetValues("Retry-After", out _), Is.True);
        await AssertGenericTooManyRequestsResponse(blockedResponse);
    }

    private static async Task<IHost> CreateHostAsync(Action<RateLimitingOptions>? configureOptions = null)
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services
                        .AddAuthentication(TestAuthenticationHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                            TestAuthenticationHandler.SchemeName,
                            _ => { });
                    services.AddAuthorization();
                    services.AddControllers().AddApplicationPart(typeof(TestAuthRateLimitController).Assembly);
                    services.AddAutomatonRateLimiting();
                    if (configureOptions is not null)
                    {
                        services.Configure(configureOptions);
                    }
                });

                webBuilder.Configure(app =>
                {
                    app.UseAuthentication();
                    app.UseRouting();
                    app.UseRateLimiter();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapControllers());
                });
            })
            .StartAsync();

        return host;
    }

    private static async Task AssertGenericTooManyRequestsResponse(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        var message = json.RootElement.GetProperty("message").GetString();

        Assert.That(message, Is.EqualTo("Too many requests. Please try again later."));
    }
}

[ApiController]
[Route("test-auth")]
public class TestAuthRateLimitController : ControllerBase
{
    [HttpGet("global-anonymous")]
    public IActionResult GlobalAnonymous()
    {
        return Ok();
    }

    [HttpGet("global-authenticated")]
    [Authorize(AuthenticationSchemes = TestAuthenticationHandler.SchemeName)]
    public IActionResult GlobalAuthenticated()
    {
        return Ok();
    }

    [HttpPost("login")]
    [EnableRateLimiting(RateLimitPolicyNames.AuthLogin)]
    [EmailRateLimit("auth:login:email", 10, 900)]
    public IActionResult Login([FromBody] SignInUserCommand command)
    {
        return Ok(new { email = command.Email });
    }

    [HttpPost("forgot-password")]
    [EnableRateLimiting(RateLimitPolicyNames.AuthForgotPassword)]
    [EmailRateLimit("auth:forgot-password:email", 3, 3600)]
    public IActionResult ForgotPassword([FromBody] ForgotUserPasswordCommand command)
    {
        return NoContent();
    }

    [HttpPost("reset-password")]
    [EnableRateLimiting(RateLimitPolicyNames.AuthResetPassword)]
    public IActionResult ResetPassword([FromBody] TestResetPasswordCommand command)
    {
        return NoContent();
    }

    [HttpPost("refresh-token")]
    [EnableRateLimiting(RateLimitPolicyNames.AuthRefreshToken)]
    public IActionResult RefreshToken([FromBody] RefreshAccessTokenCommand command)
    {
        return Ok(new { token = command.Token });
    }

    [HttpPost("apikey-validate")]
    [EnableRateLimiting(RateLimitPolicyNames.ApiKeyValidation)]
    public IActionResult ValidateApiKey([FromBody] TestValidateApiKeyRequest request)
    {
        return Ok(new { request.ApiKey });
    }

    [HttpPost("public/controller-execute")]
    [EnableRateLimiting(RateLimitPolicyNames.PublicAppControllerExecution)]
    public IActionResult PublicControllerExecute([FromBody] TestPublicExecutionRequest request)
    {
        return Ok(new { request.Value });
    }

    [HttpPost("public/flow-execute")]
    [EnableRateLimiting(RateLimitPolicyNames.PublicAppFlowExecution)]
    public IActionResult PublicFlowExecute([FromBody] TestPublicExecutionRequest request)
    {
        return Ok(new { request.Value });
    }

    [HttpPost("public/step-execute")]
    [EnableRateLimiting(RateLimitPolicyNames.PublicAppStepExecution)]
    public IActionResult PublicStepExecute([FromBody] TestPublicExecutionRequest request)
    {
        return Ok(new { request.Value });
    }
}

public class TestValidateApiKeyRequest
{
    public required string ApiKey { get; set; }
}

public class TestResetPasswordCommand
{
    public required string Email { get; set; }
    public required string Password { get; set; }
    public required string Code { get; set; }
}

public class TestPublicExecutionRequest
{
    public required string Value { get; set; }
}

public class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "TestAuth";

    public TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Test-User", out var testUserHeader) ||
            string.IsNullOrWhiteSpace(testUserHeader))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, testUserHeader.ToString()),
            new Claim(ClaimTypes.Name, testUserHeader.ToString())
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
