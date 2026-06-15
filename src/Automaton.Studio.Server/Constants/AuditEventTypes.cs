namespace Automaton.Studio.Server.Constants;

public static class AuditEventTypes
{
    public const string ApiKeyCreated = "ApiKeyCreated";
    public const string ApiKeyDeleted = "ApiKeyDeleted";
    public const string AppCreated = "AppCreated";
    public const string AppDeleted = "AppDeleted";
    public const string AppPublished = "AppPublished";
    public const string AppUnpublished = "AppUnpublished";
    public const string AppForked = "AppForked";
    public const string RunnerAssignedToApp = "RunnerAssignedToApp";
    public const string RunnerUnassignedFromApp = "RunnerUnassignedFromApp";
    public const string RunnerCreated = "RunnerCreated";
    public const string RunnerDeleted = "RunnerDeleted";
    public const string RunnerAssignedToProject = "RunnerAssignedToProject";
    public const string RunnerUnassignedFromProject = "RunnerUnassignedFromProject";
    public const string UserDisabled = "UserDisabled";
    public const string UserEnabled = "UserEnabled";
    public const string UserDeleted = "UserDeleted";
    public const string UserRoleChanged = "UserRoleChanged";
}
