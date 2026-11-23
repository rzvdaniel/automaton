using System.ComponentModel.DataAnnotations;

namespace Automaton.Api.Entities;

/// <summary>A system administrator's approval of one app permission (<c>runner:maintain</c> or <c>runner:host-exec</c>) for one Runner's machine.</summary>
public sealed class AppRunnerPermission
{
    [Key]
    public Guid Id { get; set; }
    public Guid AppId { get; set; }
    public Guid RunnerId { get; set; }
    [Required, MaxLength(100)]
    public string Permission { get; set; } = string.Empty;
    public DateTime ApprovedAtUtc { get; set; }
    /// <summary>For <c>runner:host-exec</c>: the app's owner and code (version, commit, last deployment) the approval was given for; a change suspends it.</summary>
    [MaxLength(400)]
    public string? CodeFingerprint { get; set; }
    /// <summary>The same, in words, for the approval screen.</summary>
    [MaxLength(200)]
    public string? ApprovedFor { get; set; }
}
