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

        /// <summary>
        /// Optional public username for public profile URLs. 
        /// If not provided, will be auto-generated from display name.
        /// Must be 3-32 characters, alphanumeric with hyphens/underscores.
        /// </summary>
        public string? PublicUsername { get; }

        [JsonConstructor]
        public RegisterUserCommand(string name, string email, string password, string? publicUsername = null)
        {
            Name = name;
            Email = email;
            Password = password;
            PublicUsername = publicUsername;
        }
    }

    /// <summary>
    /// Request model for updating public username
    /// </summary>
    public class UpdatePublicUsernameRequest
    {
        [Required(ErrorMessage = "Public username is required")]
        public string? NewPublicUsername { get; set; }
    }
}