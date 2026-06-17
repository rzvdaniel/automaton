using MediatR;
using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;

namespace Automaton.Api.Application.Commands
{
    public class UpdateUserInfoCommand : IRequest
    {
        [JsonIgnore]
        public Guid? Id { get; set; }

        [Required(ErrorMessage = "Name is mandatory")]
        [MaxLength(length: 100, ErrorMessage = "Maximum length is 100 characters")]
        public string Name { get; }

        [Required(ErrorMessage = "Email address value is mandatory")]
        [EmailAddress(ErrorMessage = "Invalid email address")]
        public string? Email { get; }

        [JsonConstructor]
        public UpdateUserInfoCommand(Guid? id, string name, string email)
        {
            this.Id = id;
            this.Name = name;
            this.Email = email;
        }
    }
}
