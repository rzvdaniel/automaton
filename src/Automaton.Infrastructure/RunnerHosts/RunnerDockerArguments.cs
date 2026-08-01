namespace Automaton.Infrastructure.RunnerHosts;

public static class RunnerDockerArguments
{
    public const string PrivilegedHost =
        "--privileged --user 0 --pid host --network host " +
        "-v /var/run/docker.sock:/var/run/docker.sock -v /:/host";
}
