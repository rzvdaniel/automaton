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

    private static (AccountController Controller, StubMediator Mediator) CreateController()
    {
        var mediator = new StubMediator();
        var services = new ServiceCollection().AddSingleton<IMediator>(mediator).BuildServiceProvider();
        var configuration = new ConfigurationService(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AppConfig:UserSignUp"] = "false" }).Build());
        var controller = new AccountController(configuration, null!, null!, null!, new RefreshTokenCookieService())
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
