using System.ComponentModel.DataAnnotations;

namespace Automaton.Api.Entities;

/// <summary>A system administrator's approval of one app permission (today only <c>runner:maintain</c>) for one Runner's machine.</summary>
public sealed class AppRunnerPermission
{
    [Key]
    public Guid Id { get; set; }
    public Guid AppId { get; set; }
    public Guid RunnerId { get; set; }
    [Required, MaxLength(100)]
    public string Permission { get; set; } = string.Empty;
    public DateTime ApprovedAtUtc { get; set; }
}
