namespace Automaton.Infrastructure.RunnerHosts;

public static class RunnerDockerArguments
{
    public const string PrivilegedHostApplication =
        "--privileged --user 0 --pid host " +
        "-v /var/run/docker.sock:/var/run/docker.sock -v /:/host";

    public const string InfrastructureHost =
        PrivilegedHostApplication + " --network host";
}
