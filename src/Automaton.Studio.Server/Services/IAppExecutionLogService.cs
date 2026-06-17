using Automaton.Api.Models;

namespace Automaton.Api.Services;

public interface IAppExecutionLogService
{
    Task LogExecutionAsync(AppExecutionLogRequest request);
    Task<PagedResult<AppExecutionLogDto>> GetLogsAsync(Guid appId, AppExecutionLogQuery query);
}
