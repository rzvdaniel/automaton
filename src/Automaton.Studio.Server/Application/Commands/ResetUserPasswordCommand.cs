using MediatR;
using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;

namespace Automaton.Studio.Server.Core.Commands;

public class ResetUserPasswordCommand : IRequest
{
    [EmailAddress(ErrorMessage = "Invalid email address")]
    public string Email { get; }

    [Required(ErrorMessage = "Password value is mandatory")]
    public string Password { get; }

    [Required]
    public string Code { get; set; }

    [JsonConstructor]
    public ResetUserPasswordCommand(string email, string password, string code)
    {
        this.Email = email;
        Password = password;
        Code = code;
    }
}

