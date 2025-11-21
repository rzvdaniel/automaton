using Automaton.Studio.Server.Models;
using Automaton.Studio.Server.Services.Interfaces;

namespace Automaton.Studio.Server.Services;

public class NoOpEmailService : IEmailService
{
    private readonly ILogger<NoOpEmailService> _logger;
    private readonly IWebHostEnvironment _env;

    public NoOpEmailService(ILogger<NoOpEmailService> logger, IWebHostEnvironment env)
    {
        _logger = logger;
        _env = env;
    }

    public Task<bool> Send(EmailData email, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Skipping email send in {Environment}. Recipients: {Recipients}", _env.EnvironmentName, string.Join(", ", email.To.Select(t => t.Email)));
        // Return true so app flow continues, but consider returning a richer result in some apps
        return Task.FromResult(true);
    }
}
