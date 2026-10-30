using Automaton.Core.Enums;

namespace Automaton.Api.Models
{
    public class RecentActivity
    {
        public Guid Id { get; set; }
        public ActivityType ActivityType { get; set; }
        public string EntityName { get; set; } = string.Empty;
        public Guid? EntityId { get; set; }
        public string Description { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public Guid UserId { get; set; }
        public string UserName { get; set; } = string.Empty;
    }
}
