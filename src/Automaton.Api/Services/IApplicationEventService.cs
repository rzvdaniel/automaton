using Automaton.Api.Models;

namespace Automaton.Api.Services;

public interface IApplicationEventService
{
    Task WriteAsync(ApplicationEventWriteRequest request, CancellationToken cancellationToken = default);
    Task<PagedResult<ApplicationEventDto>> GetEventsAsync(Guid appId, ApplicationEventQuery query, CancellationToken cancellationToken = default);
}
