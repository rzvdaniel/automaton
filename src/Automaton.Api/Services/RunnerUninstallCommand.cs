namespace Automaton.Api.Services;

/// <summary>Builds an installation-scoped host command without accepting caller-supplied Docker arguments.</summary>
internal static class RunnerUninstallCommand
{
    public static string Build(Guid runnerId, string? installationId)
    {
        if (installationId is not { Length: 35 } || !installationId.StartsWith("sh_", StringComparison.Ordinal)
            || !Guid.TryParseExact(installationId.AsSpan(3), "N", out _))
            throw new InvalidOperationException("This Runner has no valid Machine Management installation identity. Inspect and uninstall it on its host before removing its registration.");
        var runnerContainer = $"automaton-self-hosted-{installationId[3..15]}";
        var staticContainer = $"automaton-static-{installationId[3..]}";
        var volume = $"automaton-machine-runner-{installationId}-workspace";
        var network = $"automaton-self-hosted-{installationId}-runtime";
        return $$$"""
            /bin/sh <<'AUTOMATON_RUNNER_UNINSTALL'
            set -eu
            docker info >/dev/null
            # Refuse orphaned workloads rather than destroying their data.
            workloads=$(docker ps -aq --filter 'label=com.automaton.machine-runner-id={{{runnerId:D}}}')
            actions=$(docker ps -aq --filter 'label=automaton.machine-runner-id={{{runnerId:N}}}')
            if [ -n "$workloads$actions" ]; then
              echo 'App or Action containers still exist. Clean up those workloads before uninstalling.' >&2
              exit 1
            fi
            app_data=$(docker volume ls --quiet --filter 'label=com.automaton.managed=true' --filter 'label=com.automaton.resource=compose-app-volume')
            for app_volume in $app_data; do
              owner=$(docker volume inspect --format '{{index .Labels "com.automaton.machine-runner-id"}}' "$app_volume")
              if ! printf '%s\n' "$owner" | grep -Eiq '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$' || printf '%s\n' "$owner" | grep -Fxiq '{{{runnerId:D}}}'; then
                echo 'App data volumes belong to this Runner or lack a verified machine identity. Inspect and clean up retained app data before uninstalling.' >&2
                exit 1
              fi
            done
            containers=$(docker ps -a --format '{{.Names}}')
            if printf '%s\n' "$containers" | grep -Fxq '{{{runnerContainer}}}'; then
              identity=$(docker inspect --format '{{range .Config.Env}}{{println .}}{{end}}' '{{{runnerContainer}}}')
              printf '%s\n' "$identity" | grep -Fxq 'AUTOMATON_RUNNER_INSTALLATION_ID={{{installationId}}}'
              if ! identity=$(docker cp '{{{runnerContainer}}}:/app/config/runner.json' - | tar -xOf -); then
                echo 'Cannot verify the management container Runner identity; it was preserved.' >&2
                exit 1
              fi
              printf '%s\n' "$identity" | grep -Eiq '"RunnerId"[[:space:]]*:[[:space:]]*"{{{runnerId:D}}}"'
            fi
            if printf '%s\n' "$containers" | grep -Fxq '{{{staticContainer}}}'; then
              owner=$(docker inspect --format '{{index .Config.Labels "automaton.machine-runner-id"}}' '{{{staticContainer}}}')
              [ "$owner" = '{{{runnerId:D}}}' ]
              resource=$(docker inspect --format '{{index .Config.Labels "automaton.resource"}}' '{{{staticContainer}}}')
              [ "$resource" = 'machine-static-server' ]
            fi
            # Stop the management process before removing its shared static server.
            if printf '%s\n' "$containers" | grep -Fxq '{{{runnerContainer}}}'; then
              docker rm --force '{{{runnerContainer}}}'
            fi
            if printf '%s\n' "$containers" | grep -Fxq '{{{staticContainer}}}'; then
              docker rm --force '{{{staticContainer}}}'
            fi
            volumes=$(docker volume ls --format '{{.Name}}')
            if printf '%s\n' "$volumes" | grep -Fxq '{{{volume}}}'; then
              identity=$(docker volume inspect --format '{{index .Labels "automaton.installation-id"}}' '{{{volume}}}')
              [ "$identity" = '{{{installationId}}}' ]
              docker volume rm '{{{volume}}}'
            fi
            networks=$(docker network ls --format '{{.Name}}')
            if printf '%s\n' "$networks" | grep -Fxq '{{{network}}}'; then
              docker network rm '{{{network}}}'
            fi
            echo 'Runner host cleanup completed. Confirm removal in Studio to revoke its credentials.'
            echo 'The host installation/configuration directory was retained. Remove it manually after confirming credential revocation in Studio.'
            AUTOMATON_RUNNER_UNINSTALL
            """;
    }
}
