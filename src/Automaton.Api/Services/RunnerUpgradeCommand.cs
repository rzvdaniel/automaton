using System.Text.RegularExpressions;

namespace Automaton.Api.Services;

/// <summary>
/// Builds a host command that recreates one Machine Management Runner container on a new image,
/// keeping its enrolled identity, configuration, workspace volume and network.
/// </summary>
internal static partial class RunnerUpgradeCommand
{
    public static string Build(Guid runnerId, string? installationId, string image, string runnerName)
    {
        if (string.IsNullOrWhiteSpace(runnerName) || runnerName.Any(char.IsControl))
            throw new InvalidOperationException("The Runner name cannot be used in an upgrade command. Rename the Runner in Studio, then retry.");
        if (installationId is not { Length: 35 } || !installationId.StartsWith("sh_", StringComparison.Ordinal)
            || !Guid.TryParseExact(installationId.AsSpan(3), "N", out _))
            throw new InvalidOperationException("This Runner has no valid Machine Management installation identity. Reinstall it from Studio instead.");
        if (!ImagePattern().IsMatch(image))
            throw new InvalidOperationException("The configured Runner image (HostedRunners:DockerImage) is not a valid image reference.");
        var container = $"automaton-self-hosted-{installationId[3..15]}";
        // The server's current name: a Runner renamed in Studio must not restart under its old name.
        var nameLine = ShellQuote($"AUTOMATON_RUNNER_NAME={runnerName}");
        return $$$"""
            /bin/sh <<'AUTOMATON_RUNNER_UPGRADE'
            set -eu
            container='{{{container}}}'
            image='{{{image}}}'
            docker info >/dev/null
            if ! docker inspect "$container" >/dev/null 2>&1; then
              echo "Runner container $container was not found on host $(hostname). Run this command on the Runner's server." >&2
              exit 1
            fi
            if ! docker inspect --format '{{range .Config.Env}}{{println .}}{{end}}' "$container" | grep -Fxq 'AUTOMATON_RUNNER_INSTALLATION_ID={{{installationId}}}'; then
              echo 'The container belongs to another Runner installation; it was not changed.' >&2
              exit 1
            fi
            if ! identity=$(docker cp "$container:/app/config/runner.json" - | tar -xOf -) \
              || ! printf '%s\n' "$identity" | grep -Eiq '"RunnerId"[[:space:]]*:[[:space:]]*"{{{runnerId:D}}}"'; then
              echo 'Cannot verify the Runner identity; the container was not changed.' >&2
              exit 1
            fi
            docker pull "$image" >/dev/null
            resolved=$(docker image inspect --format '{{index .RepoDigests 0}}' "$image")
            case "$resolved" in *@sha256:*) ;; *) echo 'The Runner image did not resolve to an immutable digest.' >&2; exit 1 ;; esac
            if [ "$(docker inspect --format '{{.Image}}' "$container")" = "$(docker image inspect --format '{{.Id}}' "$image")" ]               && docker inspect --format '{{range .Config.Env}}{{println .}}{{end}}' "$container" | grep -Fxq {{{nameLine}}}; then
              echo 'The Runner already uses the current image.'
              exit 0
            fi
            network=$(docker inspect --format '{{.HostConfig.NetworkMode}}' "$container")
            config=$(docker inspect --format '{{range .Mounts}}{{if eq .Destination "/app/config"}}{{.Source}}{{end}}{{end}}' "$container")
            workspace=$(docker inspect --format '{{range .Mounts}}{{if eq .Destination "/automaton/storage"}}{{.Name}}{{end}}{{end}}' "$container")
            groups=$(docker inspect --format '{{range .HostConfig.GroupAdd}}--group-add {{.}} {{end}}' "$container")
            if [ -z "$config" ] || [ -z "$workspace" ]; then
              echo 'The Runner container has unexpected mounts; reinstall it from Studio instead.' >&2
              exit 1
            fi
            umask 077
            env_file=$(mktemp)
            trap 'rm -f "$env_file"' EXIT
            docker inspect --format '{{range .Config.Env}}{{println .}}{{end}}' "$container" \
              | grep '^AUTOMATON_' | grep -Ev '^AUTOMATON_RUNNER_(IMAGE|IMAGE_DIGEST|NAME)=' > "$env_file"
            printf 'AUTOMATON_RUNNER_IMAGE=%s\nAUTOMATON_RUNNER_IMAGE_DIGEST=%s\n' "$resolved" "$resolved" >> "$env_file"
            printf '%s\n' {{{nameLine}}} >> "$env_file"
            previous="$container-previous"
            docker rm --force "$previous" >/dev/null 2>&1 || true
            docker stop "$container" >/dev/null
            docker rename "$container" "$previous"
            # $groups is intentionally split into separate --group-add arguments.
            connected=false
            if docker run -d --name "$container" --restart unless-stopped --network "$network" $groups \
                --env-file "$env_file" -v "$config:/app/config" -v "$workspace:/automaton/storage" \
                -v /var/run/docker.sock:/var/run/docker.sock "$resolved" >/dev/null; then
              echo 'Waiting for the Runner to connect to Automaton...'
              attempts=0
              while [ "$attempts" -lt 45 ]; do
                attempts=$((attempts + 1))
                sleep 2
                # A container that starts and then fails registration restarts; treat that as failure.
                [ "$(docker inspect --format '{{.RestartCount}}' "$container")" = 0 ] || break
                if docker logs "$container" 2>&1 | grep -q 'Connected to server successfully'; then
                  connected=true
                  break
                fi
              done
            fi
            if [ "$connected" = true ]; then
              docker rm "$previous" >/dev/null
              echo "Runner upgraded to $resolved. It reconnects to Automaton with its existing identity."
            else
              echo 'The upgraded Runner did not connect; restoring the previous container.' >&2
              docker logs --tail 40 "$container" 2>&1 | sed -E 's/(re|ak)_[A-Za-z0-9_-]+/[REDACTED]/g' >&2 || true
              docker rm --force "$container" >/dev/null 2>&1 || true
              docker rename "$previous" "$container"
              docker start "$container" >/dev/null
              exit 1
            fi
            AUTOMATON_RUNNER_UPGRADE
            """;
    }

    private static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    [GeneratedRegex(@"^[a-z0-9][a-z0-9._/:-]*(@sha256:[a-f0-9]{64})?$")]
    private static partial Regex ImagePattern();
}
