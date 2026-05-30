using Automaton.Studio.Server.Models;
using MediatR;

namespace AuthServer.Core.Queries
{
    public class FilterUserQuery : IRequest<PagedResult<UserDetails>>
    {
        public string? Search { get; set; }
        public string? Name { get; set; }
        public string? Email { get; set; }
        public string? RegistrationProvider { get; set; }
        public bool? EmailConfirmed { get; set; }
        public bool? IsLocked { get; set; }
        public DateTime? RegisteredFromUtc { get; set; }
        public DateTime? RegisteredToUtc { get; set; }
        public string? SortField { get; set; }
        public int? SortOrder { get; set; }
        public int PageIndex { get; set; }
        public int PageSize { get; set; }
    }
}
