using MediatR;
using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;

namespace Automaton.Studio.Server.Core.Commands;

public class ForgotUserPasswordCommand : IRequest
{
    [EmailAddress(ErrorMessage = "Invalid email address")]
    public string Email { get; }

    public string? TurnstileToken { get; }

    [JsonConstructor]
    public ForgotUserPasswordCommand(string email, string? turnstileToken = null)
    {
        Email = email;
        TurnstileToken = turnstileToken;
    }
}
