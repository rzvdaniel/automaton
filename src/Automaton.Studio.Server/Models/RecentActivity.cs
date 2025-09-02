using Automaton.Core.Enums;

namespace Automaton.Studio.Server.Models
{
    public class RecentActivity
    {
        public Guid Id { get; set; }
        public ActivityType ActivityType { get; set; }
        public string EntityName { get; set; }
        public Guid? EntityId { get; set; }
        public string Description { get; set; }
        public DateTime CreatedAt { get; set; }
        public Guid UserId { get; set; }
        public string UserName { get; set; }
    }
}