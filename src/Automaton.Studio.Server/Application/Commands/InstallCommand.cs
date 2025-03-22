using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;

namespace Automaton.Studio.Server.Core.Commands
{
    public class RegisterUserCommand
    {
        [Required(ErrorMessage = "Name is mandatory")]
        public string? Name { get; }

        [Required(ErrorMessage = "Email address is mandatory")]
        [EmailAddress(ErrorMessage = "Invalid email address")]
        public string? Email { get; }

        [Required(ErrorMessage = "Password is mandatory")]
        public string? Password { get; }

        [JsonConstructor]
        public RegisterUserCommand(string name, string email, string password)
        {
            Name = name;
            Email = email;
            Password = password;
        }
    }
}