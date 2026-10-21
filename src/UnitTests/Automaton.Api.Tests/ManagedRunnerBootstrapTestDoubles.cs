using Automaton.Api.Constants;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;

namespace Automaton.Api.Tests;

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
