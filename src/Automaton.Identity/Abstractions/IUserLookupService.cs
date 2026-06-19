using Automaton.Api.Entities;

namespace Automaton.Identity.Abstractions;

public interface IUserLookupService
{
    Task<ApplicationUser> GetByIdAsync(Guid userId, CancellationToken cancellationToken = default);
}
