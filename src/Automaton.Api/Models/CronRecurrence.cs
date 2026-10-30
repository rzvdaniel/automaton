using Automaton.Api.Enums;

namespace Automaton.Api.Models;

public class CronRecurrence
{
    public CronType? CronType { get; set; }
    public int? Minute { get; set; } = 0;
    public int? Hour { get; set; } = 0;
    public int? Day { get; set; } = 1;
    public DayOfWeek? DayOfWeek { get; set; } = System.DayOfWeek.Monday;
    public int? Month { get; set; } = 1;

    public CronRecurrence()
    {
        CronType = Enums.CronType.Never;
        Hour = 0;
        Day = 1;
        Month = 1;
        DayOfWeek = System.DayOfWeek.Monday;
    }

    public CronRecurrence(CronType cronType) : base()
    {
        CronType = cronType;
    }
}
