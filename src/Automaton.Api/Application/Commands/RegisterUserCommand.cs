using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;

namespace Automaton.Api.Application.Commands
{
    public class InstallCommand
    {
        [Required(ErrorMessage = "Name is mandatory")]
        public string? Name { get; set; }

        [Required(ErrorMessage = "Email address is mandatory")]
        [EmailAddress(ErrorMessage = "Invalid email address")]
        public string? Email { get; set; }

        [Required(ErrorMessage = "Password is mandatory")]
        public string? Password { get; set; }
    }
}