using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Automaton.Api.Services.Interfaces;
using Automaton.Api.Application.Commands;
using Automaton.Api.Controllers;
using Automaton.Api.Services;
using Automaton.Api.Security;
namespace Automaton.Api.Tests;

[TestFixture]
public class AccountControllerTurnstileTests
{
    [Test]
    public async Task RegisterUser_WhenTurnstileValidationFails_ReturnsBadRequest()
    {
        var controller = CreateController(new StubTurnstileService(false));

        var result = await controller.RegisterUser(new RegisterUserCommand("User", "user@example.com", "Password1!", "user", "bad-token"));

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task ForgotUserPassword_WhenTurnstileValidationFails_ReturnsBadRequest()
    {
        var controller = CreateController(new StubTurnstileService(false));

        var result = await controller.ForgotUserPassword(new ForgotUserPasswordCommand("user@example.com", "bad-token"), CancellationToken.None);

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task ForgotUserPassword_WhenTurnstileValidationSucceeds_DelegatesToMediator()
    {
        var mediator = new StubMediator();
        var controller = CreateController(new StubTurnstileService(true), mediator);

        var result = await controller.ForgotUserPassword(new ForgotUserPasswordCommand("user@example.com", "good-token"), CancellationToken.None);

        Assert.That(result, Is.InstanceOf<NoContentResult>());
        Assert.That(mediator.LastRequest, Is.InstanceOf<ForgotUserPasswordCommand>());
    }

    private static AccountController CreateController(ICloudflareTurnstileService turnstileService, IMediator? mediator = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AppConfig:UserSignUp"] = "true",
                ["AppConfig:WebsiteUrl"] = "https://localhost:5173"
            })
            .Build();

        var services = new ServiceCollection();
        if (mediator != null)
        {
            services.AddSingleton(mediator);
        }

        var controller = new AccountController(
            new ConfigurationService(configuration),
            null!,
            null!,
            null!,
            turnstileService,
            null!,
            new RefreshTokenCookieService(),
            null!,
            NullLogger<AccountController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    RequestServices = services.BuildServiceProvider(),
                    Connection =
                    {
                        RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.5")
                    }
                }
            }
        };

        return controller;
    }

    private sealed class StubTurnstileService(bool shouldPass) : ICloudflareTurnstileService
    {
        public Task<bool> ValidateAsync(string? token, string? remoteIp, CancellationToken cancellationToken)
        {
            return Task.FromResult(shouldPass);
        }
    }

    private sealed class StubMediator : IMediator
    {
        public object? LastRequest { get; private set; }

        public Task Publish(object notification, CancellationToken cancellationToken = default)
        {
            LastRequest = notification;
            return Task.CompletedTask;
        }

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
        {
            LastRequest = notification;
            return Task.CompletedTask;
        }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(default(TResponse)!);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            LastRequest = request;
            return Task.CompletedTask;
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult<object?>(null);
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return EmptyAsync<TResponse>();
        }

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return EmptyAsync<object?>();
        }

        private static async IAsyncEnumerable<T> EmptyAsync<T>()
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
