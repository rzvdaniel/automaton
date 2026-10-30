using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;

namespace Automaton.Api.Services;

public class NoOpEmailService : IEmailService
{
    private readonly ILogger<NoOpEmailService> _logger;
    private readonly IWebHostEnvironment _env;

    public NoOpEmailService(ILogger<NoOpEmailService> logger, IWebHostEnvironment env)
    {
        _logger = logger;
        _env = env;
    }

    public Task<bool> Send(EmailData emailDetails, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Skipping email send in {Environment}", _env.EnvironmentName);
        // Return true so app flow continues, but consider returning a richer result in some apps
        return Task.FromResult(true);
    }
}
