# Self-Hosted Runners

Studio can create a one-time Docker installation command for the existing Automaton
Runner. The Machine Management profile installs one trusted management Runner on a
customer-owned machine. Workloads run in separate containers. This document describes
the current installation and the data customers must protect manually; machine
storage telemetry is shown in Studio for Machine Management Runners. Studio provides an
upgrade command for Machine Management installations; see
[Machine Management Runner updates](#machine-management-runner-updates).

## Installation

An authenticated user creates an installation with:

```http
POST /api/self-hosted-runners/installations
Content-Type: application/json

{"runnerName":"my-runner","applicationProfile":"MachineManagement"}
```

The response contains a one-time Docker command, an expiry time, the enrollment and
installation identifiers, and the selected profile. The command is intended to be
copied and run on the user's own Linux machine. The enrollment secret is returned
only in this response and is stored by the API as a hash.

For the supported self-hosted machine flow, open **Runners → Install Self-Hosted
Runner**, choose **Machine Management**, acknowledge the Docker access warning, and
run the generated command on the customer machine. Prepare an Ubuntu host with Docker
Engine installed and running. The generated command checks Ubuntu and Docker
availability before pulling the Runner image. ext4 is the tested storage baseline;
other filesystems supported by the customer's working Docker installation, including
XFS, do not need to be changed. The command does not install Docker, configure its
data root, or move existing Docker data. After the checks pass, it creates an
installation-scoped workspace volume and mounts it at `/automaton/storage`.

To add another machine, repeat the Studio installation flow with a distinct Runner
name on that machine. Each installation has its own identity and workspace volume;
do not clone the first machine's Runner configuration or volume. After the new Runner
is connected, assign it to the intended app deployment targets. Moving a target does
not move its host-local persistent data.

The Runner later exchanges that enrollment through the existing
`POST /api/runner-enrollments/exchange` operation. The server-side enrollment is
authoritative for ownership, purpose, capabilities, installation identity, and
application profile. Reconnect and heartbeat payloads cannot change those values.

Studio periodically shows total and free space for the installation's persistent
workspace filesystem, with the measurement time and any reporting error. This is a
capacity observation, not a disk quota or a measurement of every Docker volume. The
Runner's online state, heartbeat and software version provide the current connection
health signals.

For repository development, `scripts/setup-local-development.ps1` starts the existing local control plane; it no longer provisions a per-user Hosted Runner or changes a subscription. Install Machine Management through the same Studio flow and explicitly assign the app. Endpoint health alone does not establish installation/deployment acceptance. A remote workload machine must resolve and reach the configured development API and broker; a developer-only hostname is insufficient.

## Verified single-machine development flow

On 23 October 2026, API, Studio, Worker and the CI-produced Runner from `38d994c2`
ran with a separate development database and broker on one Ubuntu 26.04 test host.
The Docker/workspace filesystem was XFS; no filesystem migration was needed. The
Runner artifact came from Azure pipeline run 5192, with digest
`sha256:b04595d5b8cd4bca9633a8e6d9a5ba9adc2dc8d26ef049c149997a8ab29c4826`.

Use a development API hostname reachable from both the workstation and Runner,
with trusted TLS. Configure the existing `HostedRunners:ServerUrl` and `DockerImage`
installer options, plus `RabbitMq:ManagementUrl` and the execution broker's
`HostedRunnerRabbitMq` host, TLS port and server name. Give the broker a valid TLS
certificate; do not disable certificate checks. Keep database/broker resources
separate from existing installations. The repository development edge must expose
`api-live-edge`, matching `PublishedApps:RuntimeTunnelServiceName`.

Run the generated Machine Management installation command, then use the workstation
CLI with a project-local development login or its existing credential environment
variables:

```text
automaton create --name "Persistence demonstration" --json
automaton runners --json
automaton set-runner <management-runner-id> --json
automaton validate --json
automaton deploy --json --non-interactive
automaton publish --slug <development-slug> --public --yes --json --non-interactive
automaton open --public
```

The demonstrated app used an explicit runtime image, HTTP health check and data
directory. It wrote a marker only under `AUTOMATON_APP_DATA_DIR`. Changing its response
and redeploying preserved that marker. An intentionally failing replacement kept
the previous healthy app serving; the CLI amendment reports failure/exit 1 instead
of successful deployment with warnings. Restoring and redeploying the healthy app
also preserved its data. The development database had zero Hosted Runner records.
This verifies the single-user web flow, not the deferred combined Actions,
two-user isolation, cancellation or restart/replay cases.

Before pulling/building images, inspect free space on the actual Docker data and
workspace filesystems, not just `/`. In this run Docker used a separate mount;
unused build stages had to be removed before the Runner image would fit. Preserve
all running/rollback images and app/database volumes during maintenance. No updater,
backup automation or legacy migration procedure is implied by this demonstration.

## Supported profile

Machine Management is the only supported Runner profile. It has access to the Docker
socket to manage bounded application and Action containers. User app code runs inside
those containers; raw workflows and custom steps in the Runner process remain disabled.

The generated command verifies Docker and the daemon, pulls the configured image,
creates the container, checks that it remains running after startup, and prints a
bounded, credential-redacted log tail before failing non-zero for restarting, exited, dead, or missing
containers. A running container means local startup succeeded; Studio separately
reports enrollment and connection state. Self-hosted application traffic continues to
use the existing outbound Runner connection and API tunnel. Direct Traefik runtime
routes remain a Hosted Runner feature backed by `HostedRunner.RuntimeEndpoint`.

## What to back up manually

There is no Automaton backup scheduler or restore tool for self-hosted installations.
Customers must protect and test recoverable copies of:

- The control-plane database and the API's configured persistent app/source storage.
- Required API encryption/signing keys and deployment configuration, stored separately
  from the database backup and protected as credentials.
- On each Machine Management host, `/opt/automaton/self-hosted-runners/<installation-id>`
  and the Docker volumes labeled for that installation, including the workspace and
  app-owned persistent data volumes. The Docker data-root configuration is needed to
  locate those volumes after host recovery.

Use database-consistent backups and quiesce app writes or use an application-consistent
method for writable volumes. Keep backups off the machine they protect. A copied
volume is not a verified recovery: test restoration on an isolated replacement before
relying on a backup. The refactoring targets fresh installations and does not provide
automatic migration from legacy Hosted Runner/account-storage installations. Any
retirement, export, manual redeployment or deletion of an old installation's data is
a separate operator decision.

## Machine Management Runner updates

Upgrade a Machine Management Runner from **Studio -> Runners -> Upgrade runner**. Studio
shows a command to run on the Runner's server. It verifies the container's installation
and Runner identity, pulls the server's configured Runner image
(`HostedRunners.DockerImage`, updated by each server release), and recreates the Runner
container with the same identity, configuration, workspace volume and network. Deployed
app containers keep running. If the new container does not start, the command restores
the previous one. Rerunning it when the Runner is current makes no changes.

Upgrade Runners installed from server releases before 0.1.15: they run an older Runner
that cannot clean up, move or delete deployed apps. The install command is for enrolling
a new installation; do not rerun it as an upgrade. Automatic Runner self-update is not
provided.

## Manual testing

Use an authenticated API key with the installation endpoint, save the returned
`installationCommand`, and run it on a test Docker host. Then verify that the Runner
appears under the requesting user's Runners and that its purpose is the normal
application purpose and `MachineManagement` profile. Confirm the Runner is connected
before assigning an app environment.
