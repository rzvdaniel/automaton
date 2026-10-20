# Self-Hosted Runners

Studio can create a one-time Docker installation command for the existing Automaton
Runner. The Machine Management profile installs one trusted management Runner on a
customer-owned machine. Workloads run in separate containers. This document describes
the current installation and the data customers must protect manually; machine
storage telemetry is shown in Studio for Machine Management Runners. There is not
yet a supported update procedure for Machine Management installations.

## Installation

An authenticated user creates an installation with:

```http
POST /api/self-hosted-runners/installations
Content-Type: application/json

{"runnerName":"my-runner","applicationProfile":"Standard"}
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

## Profiles

`Standard` uses the normal application Runner container configuration and does not
mount the Docker socket, host PID namespace, or host root filesystem.

`PrivilegedHost` preserves the approved application host-access arguments:
`--privileged`, `--user 0`, `--pid host`, the Docker socket mount, and `/` mounted at
`/host`. Unlike the Infrastructure Runner, it uses Docker bridge isolation rather than
`--network host`, so its internal FastAPI (`8001`) and runtime gateway (`8080`) listeners
do not collide with other Runner containers and are not published on the host. It still registers as a normal
application Runner with the `PrivilegedHost` application profile; it is not an
Infrastructure Runner and receives no Infrastructure capabilities or authorization.

Privileged Host applications may read and modify host files, inspect host processes,
inspect and control Docker containers, affect services and system state, and access
secrets available on the host. Docker privilege is separate from Automaton API
authorization.

Self-hosted installations use their own container name, installation identity, and
configuration directory, so they can coexist with Infrastructure and Automaton-hosted
Runners on the same physical machine without sharing credentials.

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

Machine Management installations do not yet have a supported Runner update or
rollback procedure. The generated Studio command is for enrolling a new installation;
do not rerun it as an upgrade. Existing Infrastructure Runner repair/update commands
and Compose-managed Runner instructions do not apply to Machine Management
installations. Runner self-update and rollback remain deferred work.

## Manual testing

Use an authenticated API key with the installation endpoint, save the returned
`installationCommand`, and run it on a test Docker host. Then verify that the Runner
appears under the requesting user's Runners and that its purpose is the normal
application purpose. For a Privileged Host installation, verify the profile is
`PrivilegedHost` and inspect the warning before running applications on a real host.
