using Automaton.Studio.Server.Models;

namespace Automaton.Studio.Server.Services;

public interface IAppExecutionLogService
{
    Task LogExecutionAsync(AppExecutionLogRequest request);
    Task<PagedResult<AppExecutionLogDto>> GetLogsAsync(Guid appId, AppExecutionLogQuery query);
}
