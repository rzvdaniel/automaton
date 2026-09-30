using Automaton.Api.Authentication;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Data;

public abstract class ApplicationDbContextEntitySetBase1 : IdentityDbContext<ApplicationUser, ApplicationRole, Guid, IdentityUserClaim<Guid>, ApplicationUserRole, IdentityUserLogin<Guid>, IdentityRoleClaim<Guid>, IdentityUserToken<Guid>>
{
    protected ApplicationDbContextEntitySetBase1(DbContextOptions options) : base(options) { }
    public virtual DbSet<RefreshToken> RefreshTokens { get; set; }
    public virtual DbSet<Flow> Flows { get; set; }
    public virtual DbSet<FlowUser> FlowUsers { get; set; }
    public virtual DbSet<FlowExecution> FlowExecutions { get; set; }
    public virtual DbSet<FlowExecutionUser> FlowExecutionUsers { get; set; }
    public virtual DbSet<FlowExecutionLog> FlowExecutionLogs { get; set; }
    public virtual DbSet<AuditEvent> AuditEvents { get; set; }
    public virtual DbSet<Runner> Runners { get; set; }
    public virtual DbSet<RunnerConnectionSession> RunnerConnectionSessions { get; set; }
    public virtual DbSet<RunnerUser> RunnerUsers { get; set; }
    public virtual DbSet<RunnerHost> RunnerHosts { get; set; }
    public virtual DbSet<RunnerHostProvisioningOperation> RunnerHostProvisioningOperations { get; set; }
    public virtual DbSet<InfrastructureRunnerUpdateOperation> InfrastructureRunnerUpdateOperations { get; set; }
    public virtual DbSet<RunnerEnrollment> RunnerEnrollments { get; set; }
    public virtual DbSet<SelfHostedRunnerEnrollment> SelfHostedRunnerEnrollments { get; set; }
    public virtual DbSet<InfrastructureWorkflowExecution> InfrastructureWorkflowExecutions { get; set; }
    public virtual DbSet<InfrastructureWorkflowExecutionLog> InfrastructureWorkflowExecutionLogs { get; set; }
    public virtual DbSet<HostedRunner> HostedRunners { get; set; }
    public virtual DbSet<AccountStorageAllocation> AccountStorageAllocations { get; set; }
    public virtual DbSet<HostedRunnerSpecification> HostedRunnerSpecifications { get; set; }
    public virtual DbSet<HostedRunnerSpecificationRevision> HostedRunnerSpecificationRevisions { get; set; }
    public virtual DbSet<HostedRunnerPlatformSettings> HostedRunnerPlatformSettings { get; set; }
    public virtual DbSet<HostedRunnerDeployment> HostedRunnerDeployments { get; set; }
    public virtual DbSet<HostedRunnerStorageObservation> HostedRunnerStorageObservations { get; set; }
    public virtual DbSet<HostedRunnerStorageVerification> HostedRunnerStorageVerifications { get; set; }
    public virtual DbSet<HostContainerExecutionOperation> HostContainerExecutionOperations { get; set; }
    public virtual DbSet<CustomStep> CustomSteps { get; set; }
    public virtual DbSet<CustomStepUser> CustomStepUsers { get; set; }
    public virtual DbSet<Schedule> Schedules { get; set; }
    public virtual DbSet<ScheduleUser> ScheduleUsers { get; set; }
    public virtual DbSet<RecentActivity> RecentActivities { get; set; }
    public virtual DbSet<ApiKey> ApiKeys { get; set; }
    public virtual DbSet<ApiKeyUser> ApiKeyUsers { get; set; }
    public virtual DbSet<CliAuthorizationRequest> CliAuthorizationRequests { get; set; }
    public virtual DbSet<AiApiKey> AiApiKeys { get; set; }
    public virtual DbSet<AiProvider> AiProviders { get; set; }
    public virtual DbSet<AiModel> AiModels { get; set; }
    public virtual DbSet<AiGuidanceDocument> AiGuidanceDocuments { get; set; }
    public virtual DbSet<WebApp> WebApps { get; set; }
}

public abstract class ApplicationDbContextEntitySetBase2 : ApplicationDbContextEntitySetBase1
{
    protected ApplicationDbContextEntitySetBase2(DbContextOptions options) : base(options) { }
    public virtual DbSet<AppDeploymentTarget> AppDeploymentTargets { get; set; }
    public virtual DbSet<DeploymentOperation> DeploymentOperations { get; set; }
    public virtual DbSet<MarketplaceCategory> MarketplaceCategories { get; set; }
    public virtual DbSet<WebAppStar> WebAppStars { get; set; }
    public virtual DbSet<WebAppScreenshot> WebAppScreenshots { get; set; }
    public virtual DbSet<WebAppUser> WebAppUsers { get; set; }
    public virtual DbSet<WebAppStep> WebAppSteps { get; set; }
    public virtual DbSet<AppVersion> AppVersions { get; set; }
    public virtual DbSet<AppVersionStep> AppVersionSteps { get; set; }
    public virtual DbSet<AppModuleExecution> AppModuleExecutions { get; set; }
    public virtual DbSet<ActionSchedule> ActionSchedules { get; set; }
    public virtual DbSet<ActionScheduleOccurrence> ActionScheduleOccurrences { get; set; }
    public virtual DbSet<SecretDefinition> SecretDefinitions { get; set; }
    public virtual DbSet<SecretVersion> SecretVersions { get; set; }
    public virtual DbSet<ExecutionSecretGrant> ExecutionSecretGrants { get; set; }
    public virtual DbSet<Setting> Settings { get; set; }
    public virtual DbSet<SensitiveVariable> SensitiveVariables { get; set; }
    public virtual DbSet<SensitiveVariableUser> SensitiveVariableUsers { get; set; }
    public virtual DbSet<UserGitHubToken> UserGitHubTokens { get; set; }
    public virtual DbSet<Project> Projects { get; set; }
    public virtual DbSet<ProjectMember> ProjectMembers { get; set; }
    public virtual DbSet<ProjectApp> ProjectApps { get; set; }
    public virtual DbSet<ProjectFlow> ProjectFlows { get; set; }
    public virtual DbSet<ProjectRunner> ProjectRunners { get; set; }
    public virtual DbSet<ProjectCustomStep> ProjectCustomSteps { get; set; }
    public virtual DbSet<ProjectApiKey> ProjectApiKeys { get; set; }
    public virtual DbSet<ProjectAiApiKey> ProjectAiApiKeys { get; set; }
    public virtual DbSet<InvitationToken> InvitationTokens { get; set; }
    public virtual DbSet<Organization> Organizations { get; set; }
    public virtual DbSet<OrganizationMember> OrganizationMembers { get; set; }
    public virtual DbSet<OrganizationAppAssignment> OrganizationAppAssignments { get; set; }
    public virtual DbSet<OrganizationInvitation> OrganizationInvitations { get; set; }
    public virtual DbSet<AiChatSession> AiChatSessions { get; set; }
    public virtual DbSet<AiChatMessage> AiChatMessages { get; set; }
    public virtual DbSet<AiChatSessionUser> AiChatSessionUsers { get; set; }
    public virtual DbSet<ApplicationEvent> ApplicationEvents { get; set; }
    public virtual DbSet<UserSetting> UserSettings { get; set; }
    public virtual DbSet<SubscriptionPlan> SubscriptionPlans { get; set; }
}
