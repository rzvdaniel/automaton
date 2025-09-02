using Automaton.Core.Enums;

namespace Automaton.Studio.Models
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
        
        public string ActivityTypeText => ActivityType switch
        {
            ActivityType.FlowCreated => "Flow Created",
            ActivityType.FlowUpdated => "Flow Updated", 
            ActivityType.FlowDeleted => "Flow Deleted",
            ActivityType.FlowExecuted => "Flow Executed",
            ActivityType.CustomStepCreated => "Custom Step Created",
            ActivityType.CustomStepUpdated => "Custom Step Updated",
            ActivityType.CustomStepDeleted => "Custom Step Deleted",
            ActivityType.RunnerAdded => "Runner Added",
            ActivityType.RunnerUpdated => "Runner Updated",
            ActivityType.RunnerDeleted => "Runner Deleted",
            ActivityType.ScheduleCreated => "Schedule Created",
            ActivityType.ScheduleUpdated => "Schedule Updated",
            ActivityType.ScheduleDeleted => "Schedule Deleted",
            _ => "Unknown Activity"
        };
        
        public string Icon => ActivityType switch
        {
            ActivityType.FlowCreated or ActivityType.FlowUpdated or ActivityType.FlowDeleted => "profile",
            ActivityType.FlowExecuted => "play-circle",
            ActivityType.CustomStepCreated or ActivityType.CustomStepUpdated or ActivityType.CustomStepDeleted => "experiment",
            ActivityType.RunnerAdded or ActivityType.RunnerUpdated or ActivityType.RunnerDeleted => "play-square",
            ActivityType.ScheduleCreated or ActivityType.ScheduleUpdated or ActivityType.ScheduleDeleted => "schedule",
            _ => "info-circle"
        };
        
        public string Color => ActivityType switch
        {
            ActivityType.FlowCreated or ActivityType.CustomStepCreated or ActivityType.RunnerAdded or ActivityType.ScheduleCreated => "#52c41a",
            ActivityType.FlowUpdated or ActivityType.CustomStepUpdated or ActivityType.RunnerUpdated or ActivityType.ScheduleUpdated => "#1890ff",
            ActivityType.FlowDeleted or ActivityType.CustomStepDeleted or ActivityType.RunnerDeleted or ActivityType.ScheduleDeleted => "#ff4d4f",
            ActivityType.FlowExecuted => "#722ed1",
            _ => "#8c8c8c"
        };
    }
}