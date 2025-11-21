using Automaton.Studio.Server.Models;

namespace Automaton.Studio.Server.Services.Interfaces;

public interface IEmailService
{
    Task<bool> Send(EmailData emailDetails, CancellationToken cancellationToken);
}
