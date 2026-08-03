# Self-Hosted Runners

PR 2 adds an API-only installation flow for the existing generic Automaton Runner.
It does not add Studio UI, deployment-target selection, scheduling policy, or remote
lifecycle management.

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

The Runner later exchanges that enrollment through the existing
`POST /api/runner-enrollments/exchange` operation. The server-side enrollment is
authoritative for ownership, purpose, capabilities, installation identity, and
application profile. Reconnect and heartbeat payloads cannot change those values.

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

## Manual testing

Use an authenticated API key with the installation endpoint, save the returned
`installationCommand`, and run it on a test Docker host. Then verify that the Runner
appears under the requesting user's Runners and that its purpose is the normal
application purpose. For a Privileged Host installation, verify the profile is
`PrivilegedHost` and inspect the warning before running applications on a real host.
