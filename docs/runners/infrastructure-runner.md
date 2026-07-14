# Infrastructure Runner

An Infrastructure Runner uses the same `Automaton.Runner` image and workflow
engine as every other Runner. Its elevated capabilities come from the
installation profile on the server, not from a separate Runner project.

Treat this container as having effective root access to the host. Only install
it for an administrator-owned Runner and never grant normal users access to
its workflows.

## Installation profile

Create the `RunnerHost` record in Admin -> Hosted Runners first. Create an
administrator-owned API key with Runner scope and keep it for the one-time
installation, then run
[`scripts/install-infrastructure-runner.sh`](../../scripts/install-infrastructure-runner.sh)
over SSH as root:

```bash
export AUTOMATON_SERVER_URL='https://your-configured-api-url'
export AUTOMATON_API_KEY='protected-admin-runner-key'
export AUTOMATON_RUNNER_IMAGE='your-registry/automaton-runner:tag'
export AUTOMATON_RUNNER_NAME='infrastructure-<host-name>'
export AUTOMATON_RUNNER_HOST_ID='<runner-host-id>'
bash scripts/install-infrastructure-runner.sh
```

The script installs Docker on a Debian-style host when needed, starts Docker,
pulls the image, and starts the same image with the infrastructure profile. The
equivalent container command is:

```bash
docker run -d \
  --name automaton-infrastructure-runner \
  --restart unless-stopped \
  --user 0 \
  --privileged \
  --pid=host \
  --network host \
  -v /var/run/docker.sock:/var/run/docker.sock \
  -v /:/host \
  -v automaton-infrastructure-runner-data:/automaton/workspace \
  -e AUTOMATON_SERVER_URL='https://your-configured-api-url' \
  -e AUTOMATON_API_KEY='protected-admin-runner-key' \
  -e AUTOMATON_RUNNER_NAME='infrastructure-<host-name>' \
  -e AUTOMATON_RUNNER_PURPOSE=Infrastructure \
  -e AUTOMATON_RUNNER_HOST_ID='<runner-host-id>' \
  <runner-image>
```

The API accepts `Infrastructure` purpose only when the API key belongs to an
administrator. On registration, the Runner is linked to the supplied
`RunnerHost` record. The Runner generates and persists its own identity on the
first registration. The API does not store the raw API key.

## Workflows and hosted-runner provisioning

The existing workflow and custom-step APIs already accept explicit Runner IDs,
so Studio does not need a second workflow engine or a second Runner project:

```http
POST /api/customsteps/{customStepId}/execute
{ "runnerIds": ["<infrastructure-runner-id>"] }

POST /api/flows/run
{ "flowId": "<flow-id>", "runnerIds": ["<infrastructure-runner-id>"] }
```

The same capability executes normal container commands and deliberate
`automaton-host-exec ...` commands. When a `RunnerHost` has a linked online
Infrastructure Runner, hosted-runner Docker provisioning, restart, and delete
operations are sent over its authenticated Runner connection. SSH remains the
bootstrap and emergency recovery path for hosts that are not yet linked.

For remote hosts, Automaton allocates a unique host port from
`HostedRunners:RemoteRuntimePortStart` through
`HostedRunners:RemoteRuntimePortEnd`, publishes it to the hosted Runner's
gateway port, and stores a runtime endpoint such as
`http://<runner-host-address>:20000`. The host firewall must allow that range
from the API/reverse-proxy network. Same-host deployments continue using the
Docker-network endpoint.

## Host commands

Commands executed normally still run inside the Runner container. To execute a
command against the host namespaces, infrastructure workflows can call:

```bash
automaton-host-exec systemctl restart docker
automaton-host-exec journalctl -u docker --since '1 hour ago'
automaton-host-exec apt-get update
```

The helper uses `nsenter` against host PID 1. Mounting `/host` is useful for
direct file access, but it is not itself a host execution environment.

Docker commands use the mounted host Docker socket and therefore manage the
host Docker daemon:

```bash
docker ps
docker pull <runner-image>
docker network create <network>
```

## Operational rules

- Keep SSH available as an emergency recovery channel.
- Audit and review every workflow that targets an Infrastructure Runner.
- Persist state before restarting Docker, replacing the Runner, changing
  firewall/networking, or rebooting the host.
- Wait for the Runner heartbeat after disruptive operations and report a
  visible timeout if it does not return.
- Hosted-runner provisioning uses the linked Infrastructure Runner when it is
  online; SSH remains available for bootstrap and recovery.
