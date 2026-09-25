using Automaton.Api.Config;
using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;
using Automaton.Core.Constants;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Events;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Tests;

    internal sealed class BootstrapRecordingEventPublisher : IEventPublisher
    {
        public List<object> Messages { get; } = [];

        public Task PublishAsync<T>(T message, CancellationToken cancellationToken = default)
            where T : class
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }

    internal sealed class BootstrapRecordingApplicationEventService : IApplicationEventService
    {
        public List<ApplicationEventWriteRequest> Events { get; } = [];

        public Task WriteAsync(ApplicationEventWriteRequest request, CancellationToken cancellationToken = default)
        {
            Events.Add(request);
            return Task.CompletedTask;
        }

        public Task<PagedResult<ApplicationEventDto>> GetEventsAsync(
            Guid appId,
            ApplicationEventQuery query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PagedResult<ApplicationEventDto>
            {
                Items = [],
                TotalCount = 0,
                Page = query.Page,
                PageSize = query.PageSize
            });
    }

    internal sealed class BootstrapNoOpAuditEventService : IAuditEventService
    {
        public Task WriteAsync(AuditEventWriteRequest request, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task WriteAsync(
            string action,
            string entityType,
            Guid? entityId,
            string message,
            object? metadata = null,
            string outcome = "Success",
            bool failOnError = false,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    internal sealed class BootstrapTestOptionsSnapshot<T>(T value) : IOptionsSnapshot<T>
        where T : class
    {
        public T Value { get; } = value;

        public T Get(string? name) => Value;

    }

