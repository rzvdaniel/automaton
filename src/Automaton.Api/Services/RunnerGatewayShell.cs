namespace Automaton.Api.Services;

/// <summary>
/// The shell that publishes a Runner's runtime gateway (#1181), shared by the installer and the upgrade script.
/// It needs $runtime_host_address (the private bridge address of the Runner's own network) and may be given the
/// $gateway_secret and $gateway_port of an earlier install. It sets $gateway_args, which stays empty when anything is
/// missing: the Runner then keeps serving its apps through the tunnel, so this never makes an install fail.
/// </summary>
internal static class RunnerGatewayShell
{
    public const string Setup = """
        gateway_args=''
        if [ -n "${runtime_host_address:-}" ]; then
          gateway_secret=${gateway_secret:-$(head -c 32 /dev/urandom | od -An -tx1 | tr -d ' \n')}
          if [ -z "${gateway_port:-}" ]; then
            attempt=0
            while [ "$attempt" -lt 20 ]; do
              attempt=$((attempt + 1))
              candidate=$((20000 + $(od -An -N2 -tu2 /dev/urandom | tr -d ' ') % 20000))
              if ! docker ps -a --format '{{.Ports}}' | grep -q ":$candidate->"; then gateway_port=$candidate; break; fi
            done
          fi
          case "${gateway_port:-x}${gateway_secret:-x}" in *[!0-9a-f]*|'') ;; *)
            gateway_args="-p $runtime_host_address:$gateway_port:8080 -e AUTOMATON_GATEWAY_SECRET=$gateway_secret -e AUTOMATON_RUNTIME_GATEWAY_URL=http://$runtime_host_address:$gateway_port" ;;
          esac
        fi
        """;
}
