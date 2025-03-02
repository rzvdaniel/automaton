using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;

namespace Automaton.Studio.Server.Core.Commands
{
    public class SignInUserCommand
    {
        [Required(ErrorMessage = "Email is mandatory")]
        public string Email { get; }

        [Required(ErrorMessage = "Password is mandatory")]
        public string Password { get; }

        [JsonConstructor]
        public SignInUserCommand(string email, string password)
        {
            Email = email;
            Password = password;
        }
    }
}