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

`PrivilegedHost` uses the same approved host-access Docker arguments as the existing
Infrastructure Runner: `--privileged`, `--user 0`, `--pid host`, `--network host`,
the Docker socket mount, and `/` mounted at `/host`. It still registers as a normal
application Runner with the `PrivilegedHost` application profile; it is not an
Infrastructure Runner and receives no Infrastructure capabilities or authorization.

Privileged Host applications may read and modify host files, inspect host processes,
inspect and control Docker containers, affect services and system state, and access
secrets available on the host. Docker privilege is separate from Automaton API
authorization.

Self-hosted installations use their own container name, installation identity, and
configuration directory, so they can coexist with Infrastructure and Automaton-hosted
Runners on the same physical machine without sharing credentials.

## Manual testing

Use an authenticated API key with the installation endpoint, save the returned
`installationCommand`, and run it on a test Docker host. Then verify that the Runner
appears under the requesting user's Runners and that its purpose is the normal
application purpose. For a Privileged Host installation, verify the profile is
`PrivilegedHost` and inspect the warning before running applications on a real host.
