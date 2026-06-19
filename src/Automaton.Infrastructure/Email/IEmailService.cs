using Automaton.Api.Models;

namespace Automaton.Api.Services.Interfaces;

public interface IEmailService
{
    Task<bool> Send(EmailData emailDetails, CancellationToken cancellationToken);
}
