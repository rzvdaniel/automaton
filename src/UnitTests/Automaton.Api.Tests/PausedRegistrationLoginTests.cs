using Automaton.Api.Application.Commands;
using Automaton.Api.Controllers;
using Automaton.Api.Services;
using Automaton.Api.Security;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Automaton.Api.Tests;

[TestFixture]
public class PausedRegistrationLoginTests
{
    [Test]
    public async Task LoginUser_WhenRegistrationPaused_StillIssuesExistingUserToken()
    {
        var (controller, mediator) = CreateController();
        var result = await controller.LoginUser(new SignInUserCommand("existing@example.com", "Password1!"), CancellationToken.None);
        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        Assert.That(mediator.LastRequest, Is.InstanceOf<Automaton.Api.Application.Queries.SignInUserQuery>());
        Assert.That(controller.Response.Headers.SetCookie.ToString(), Does.Contain(RefreshTokenCookieService.CookieName));
    }

    [Test]
    public async Task LoginUser_WhenCredentialsAreWrong_Returns401WithMessage()
    {
        var (controller, _) = CreateController(new Automaton.Api.Services.InvalidCredentialsException());
        var result = await controller.LoginUser(new SignInUserCommand("existing@example.com", "wrong"), CancellationToken.None);
        var unauthorized = result.Result as UnauthorizedObjectResult;
        Assert.That(unauthorized, Is.Not.Null);
        Assert.That(unauthorized!.StatusCode, Is.EqualTo(401));
        Assert.That(unauthorized.Value!.ToString(), Does.Contain("Wrong email or password."));
        Assert.That(controller.Response.Headers.SetCookie.ToString(), Is.Empty);
    }

    private static (AccountController Controller, StubMediator Mediator) CreateController(Exception? failure = null)
    {
        var mediator = new StubMediator { Failure = failure };
        var services = new ServiceCollection().AddSingleton<IMediator>(mediator).BuildServiceProvider();
        var configuration = new ConfigurationService(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AppConfig:UserSignUp"] = "false" }).Build());
        var controller = new AccountController(configuration, null!, null!, null!)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { RequestServices = services }
            }
        };
        return (controller, mediator);
    }

    private sealed class StubMediator : IMediator
    {
        public object? LastRequest { get; private set; }
        public Exception? Failure { get; init; }

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
            if (Failure is not null) throw Failure;
            if (typeof(TResponse) == typeof(Automaton.Api.Authentication.JsonWebToken))
                return Task.FromResult((TResponse)(object)new Automaton.Api.Authentication.JsonWebToken
                {
                    AccessToken = "test-access-token", RefreshToken = "test-refresh-token", UserId = Guid.NewGuid().ToString()
                });
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
