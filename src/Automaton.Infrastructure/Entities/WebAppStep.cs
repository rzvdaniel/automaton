namespace Automaton.Api.Entities;

/// <summary>
/// Junction table linking Web Apps to Custom Steps
/// </summary>
public class WebAppStep
{
    public Guid WebAppId { get; set; }
    public Guid CustomStepId { get; set; }
    public DateTime LinkedAt { get; set; }
    public Guid? LinkedBy { get; set; }

    // Navigation properties
    public virtual WebApp WebApp { get; set; } = null!;
    public virtual CustomStep CustomStep { get; set; } = null!;
    public virtual ApplicationUser? LinkedByUser { get; set; }
}
