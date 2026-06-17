namespace Automaton.Api.Services;

public class ApiInstanceIdentityService
{
    private const string ApiInstanceIdEnvVar = "AUTOMATON_API_INSTANCE_ID";
    private const string ApiColorEnvVar = "AUTOMATON_API_COLOR";

    public ApiInstanceIdentityService()
    {
        ApiInstanceId = ReadOrGenerateInstanceId();
        ApiColor = ReadApiColor();
    }

    public string ApiInstanceId { get; }

    public string? ApiColor { get; }

    private static string ReadOrGenerateInstanceId()
    {
        var configured = Environment.GetEnvironmentVariable(ApiInstanceIdEnvVar)?.Trim();
        return string.IsNullOrWhiteSpace(configured) ? Guid.NewGuid().ToString("n") : configured;
    }

    private static string? ReadApiColor()
    {
        var configured = Environment.GetEnvironmentVariable(ApiColorEnvVar)?.Trim();
        return string.IsNullOrWhiteSpace(configured) ? null : configured;
    }
}
