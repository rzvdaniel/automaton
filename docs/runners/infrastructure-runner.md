# Infrastructure Runner

An Infrastructure Runner uses the same `Automaton.Runner` image and workflow
engine as every other Runner. Its elevated capabilities come from the
installation profile on the server, not from a separate Runner project.

Treat this container as having effective root access to the host. Only install
it for an administrator-owned Runner and never grant normal users access to
its workflows.

## Installation profile

Create the `RunnerHost` record in Admin -> Hosted Runners first. In the host
row, choose **Install Runner**. Studio creates a short-lived, one-time
enrollment token bound to that host and the administrator-approved
Infrastructure capability profile. Copy the generated command and run
[`scripts/install-infrastructure-runner.sh`](../../scripts/install-infrastructure-runner.sh)
over SSH as root:

```bash
sudo bash scripts/install-infrastructure-runner.sh \
  --server-url 'https://your-configured-api-url' \
  --enrollment-token 're_one-time-token' \
  --runner-image 'your-registry/automaton-runner:tag'
```

The installer installs Docker on a Debian-style host when needed, starts
Docker, pulls the image, and starts the same image with privileged host access.
The equivalent container command contains only the server URL and one-time
enrollment token:

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
  -v /var/lib/automaton/infrastructure-runner/config:/app/config \
  -v /var/lib/automaton/infrastructure-runner/workspace:/automaton/workspace \
  -v /var/lib/automaton/infrastructure-runner/logs:/app/logs \
  -e AUTOMATON_SERVER_URL='https://your-configured-api-url' \
  -e AUTOMATON_ENROLLMENT_TOKEN='re_one-time-token' \
  <runner-image>
```

The Runner exchanges the token at `POST /api/runner-enrollments/exchange`. The
API determines the purpose and capabilities from the enrollment record; the
Runner cannot request a broader profile. The API creates the Runner, links it
to the `RunnerHost`, and issues a new Runner-scoped credential. The Runner
persists that credential and its installation profile in the mounted config
volume, so recreating the container with the same volume reconnects as the
same Runner. The raw enrollment token is hashed by the API and the raw Runner
credential is never logged or returned by later API calls.

If the enrollment command is run again after successful use, the token is
rejected. Use a new enrollment from Studio for a deliberate replacement; the
current slice does not yet implement automatic replacement or credential
rotation.

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
- Infrastructure Runners cannot be added to projects or assigned to app
  execution. Public and private app execution paths exclude them; use an
  administrator's direct Studio/API workflow or custom-step execution instead.
- Persist state before restarting Docker, replacing the Runner, changing
  firewall/networking, or rebooting the host.
- Wait for the Runner heartbeat after disruptive operations and report a
  visible timeout if it does not return.
- Hosted-runner provisioning uses the linked Infrastructure Runner when it is
  online; SSH remains available for bootstrap and recovery.
