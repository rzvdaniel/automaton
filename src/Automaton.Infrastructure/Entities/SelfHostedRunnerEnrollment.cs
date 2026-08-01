using System.ComponentModel.DataAnnotations;
using Automaton.Api.Models;

namespace Automaton.Api.Entities;

public sealed class SelfHostedRunnerEnrollment
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    public Guid UserId { get; set; }

    [Required, MaxLength(120)]
    public string RunnerName { get; set; } = string.Empty;

    [Required, MaxLength(40)]
    public string ApplicationProfile { get; set; } = string.Empty;

    [Required, MaxLength(128)]
    public string EnrollmentTokenHash { get; set; } = string.Empty;

    [Required, MaxLength(128)]
    public string InstallationId { get; set; } = string.Empty;

    [Required, MaxLength(32)]
    public string Status { get; set; } = RunnerEnrollmentStatuses.Pending;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? UsedAtUtc { get; set; }
    public Guid? RunnerId { get; set; }
}
