using Automaton.Api.Enums;
using System.Text.Json.Serialization;

namespace Automaton.Api.Models;

public class ScheduleModel
{
    [JsonRequired]
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    [JsonRequired]
    public Guid FlowId { get; set; }

    public IEnumerable<Guid> RunnerIds { get; set; } = Array.Empty<Guid>();

    public CronRecurrence CronRecurrence { get; set; }

    public string? Cron { get; set; }

    public DateTime? CreatedAt { get; set; }

    public ScheduleModel()
    {
        CronRecurrence = new CronRecurrence();
    }

    public string GetCron()
    {
        var recurrence = CronRecurrence;
        return (recurrence.CronType ?? CronType.Never) switch
        {
            CronType.Minutely => Hangfire.Cron.Minutely(),
            CronType.Hourly => GetHourlyCron(recurrence),
            CronType.Daily => GetDailyCron(recurrence),
            CronType.Weekly => GetWeeklyCron(recurrence),
            CronType.Monthly => GetMonthlyCron(recurrence),
            CronType.Yearly => GetYearlyCron(recurrence),
            _ => Hangfire.Cron.Never()
        };
    }

    private static string GetHourlyCron(CronRecurrence recurrence) =>
        Hangfire.Cron.Hourly(recurrence.Minute ?? 0);

    private static string GetDailyCron(CronRecurrence recurrence) =>
        Hangfire.Cron.Daily(hour: recurrence.Hour ?? 0, minute: recurrence.Minute ?? 0);

    private static string GetWeeklyCron(CronRecurrence recurrence) =>
        Hangfire.Cron.Weekly(
            dayOfWeek: recurrence.DayOfWeek ?? DayOfWeek.Monday,
            hour: recurrence.Hour ?? 0,
            minute: recurrence.Minute ?? 0);

    private static string GetMonthlyCron(CronRecurrence recurrence) =>
        Hangfire.Cron.Monthly(
            day: recurrence.Day ?? 1,
            hour: recurrence.Hour ?? 0,
            minute: recurrence.Minute ?? 0);

    private static string GetYearlyCron(CronRecurrence recurrence) =>
        Hangfire.Cron.Yearly(
            month: recurrence.Month ?? 1,
            day: recurrence.Day ?? 1,
            hour: recurrence.Hour ?? 0,
            minute: recurrence.Minute ?? 0);
}
