namespace Automaton.Api.Config;

public class RunnerEnrollmentOptions
{
    public const string SectionName = "RunnerEnrollment";

    public int TokenLifetimeMinutes { get; set; } = 15;
}
