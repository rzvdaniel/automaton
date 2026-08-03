# Automaton Privileged Host Apps

Use this guidance when an App needs trusted access to host metrics, Docker, operating-system information, or other host-level resources through a Privileged Host Runner.

## Core model

- A Privileged Host App is still a normal Automaton App.
- Keep frontend source under `frontend/` and backend source under `backend/`.
- Use the supported ordinary HTTP backend contract. The initial Python convention is FastAPI/ASGI with `main:app` and routes under `/api`.
- Privilege comes from assigning the App to a Runner whose application profile is `PrivilegedHost`. The App does not request privilege through `automaton.json`, a manifest, or a client-provided flag.
- Privileged Host Runners and Infrastructure Runners are different roles. An App must never target or depend on an Infrastructure Runner.
- Keep privileged Apps private unless the user explicitly chooses a safe public design. Monitoring and administration Apps should normally use a private runtime.

## Available host integration

A Privileged Host Runner is currently started with host-level Docker settings that include:

- root inside the Runner container;
- the host PID namespace;
- host networking;
- the host filesystem mounted at `/host`;
- the Docker socket mounted at `/var/run/docker.sock`.

An App backend launched by that Runner inherits those mounts and namespaces. Treat every capability as optional at runtime and detect it before use. A Standard Runner may not provide any of them.

Do not assume that utilities such as `docker`, `systemctl`, `nsenter`, `bash`, or distribution-specific commands are installed in the Runner image.

## Preferred implementation order

Use the least powerful integration that solves the task:

1. Read fixed, known files beneath `/host`, such as `/host/proc`, `/host/sys`, and `/host/etc/os-release`.
2. Use a narrowly scoped library or Unix-socket client for `/var/run/docker.sock`.
3. Use Python standard-library APIs when they expose the required information.
4. Run a fixed subprocess only when no stable file or API is suitable.

Prefer read-only parsers over shell commands for metrics and inventory.

## Fixed host-file access

- Host paths must be constants or selected from a small internal allowlist.
- Never accept a filesystem path from an HTTP request, Action input, query string, header, or frontend state.
- Never expose raw host-file contents.
- Parse only the fields the App needs and return a sanitized DTO.
- Treat missing, inaccessible, and malformed files as partial unavailability rather than a reason to crash the whole backend.
- Make the host root configurable only through trusted backend configuration for tests, with `/host` as the production default.

Example:

```python
from pathlib import Path

HOST_ROOT = Path("/host")
MEMINFO_PATH = HOST_ROOT / "proc" / "meminfo"

def read_meminfo() -> dict[str, int]:
    values: dict[str, int] = {}
    for line in MEMINFO_PATH.read_text(encoding="utf-8").splitlines():
        key, raw_value = line.split(":", 1)
        values[key] = int(raw_value.strip().split()[0]) * 1024
    return values
```

## Safe subprocess execution

Subprocess execution is a last resort. It does not create a general host shell: the command runs inside the Privileged Host Runner container, with the Runner's namespaces and mounts.

When a fixed command is genuinely required:

- use a predetermined executable;
- use a predetermined argument list;
- pass arguments as a list;
- set `shell=False`;
- set a short timeout;
- capture bounded output;
- parse only expected fields;
- handle the executable being absent;
- log detailed failures only in private backend logs;
- return a generic unavailable warning to the frontend.

```python
import subprocess

result = subprocess.run(
    ["fixed-command", "--fixed-option"],
    shell=False,
    capture_output=True,
    text=True,
    timeout=3,
    check=False,
)
```

Never:

- pass user-provided values as an executable, command, option, path, or shell fragment;
- use `shell=True`;
- use `bash -c` or `sh -c` with interpolated content;
- expose an endpoint that accepts commands;
- expose a terminal or interactive shell;
- use `chroot /host` or `nsenter` as a shortcut for normal App behavior;
- run package-management, service-management, user-management, firewall, or destructive filesystem commands unless a separately reviewed feature explicitly requires them.

## Docker access

Use `/var/run/docker.sock` only through a bounded, purpose-specific client.

- Request only the Docker endpoints required by the App.
- Apply short timeouts and bounded concurrency.
- Avoid unbounded streaming stats requests.
- Sanitize every response before returning it to the browser.
- Treat containers disappearing during collection as a normal race.
- Return partial results when Docker is unavailable.

Do not return or log:

- environment variables;
- secrets;
- labels unless explicitly allowlisted and proven safe;
- mounts or bind paths;
- raw inspect responses;
- commands or entrypoints;
- registry credentials;
- network addresses;
- authentication configuration.

Do not expose arbitrary Docker API forwarding or generic container-inspection endpoints.

## API boundary

Expose fixed application-level resources, for example:

```text
GET /api/health
GET /api/snapshot
GET /api/history
```

Do not expose APIs that accept arbitrary:

- commands;
- executable names;
- process IDs;
- Docker IDs for raw inspection;
- filesystem paths;
- service names;
- URLs to fetch from the host.

Container lifecycle operations such as start, stop, restart, remove, exec, and update are out of scope unless the user explicitly requests a separately reviewed management feature.

## Degraded behavior

Privileged integrations must fail independently.

- If `/host` is unavailable, return host metrics as unavailable while keeping the backend healthy.
- If Docker is unavailable, return a Docker warning while preserving non-Docker metrics.
- Do not return raw exceptions, stack traces, host paths, or Docker errors to the browser.
- Avoid logging the same repeated collector failure on every polling interval; log state transitions or deduplicate repeated failures.

## Security and trust

- Apps on a Privileged Host Runner may have administrator-level control over the host.
- Install and deploy only trusted source.
- Do not place unrelated or untrusted Apps on the same Privileged Host Runner.
- Never expose environment variables, credentials, API keys, enrollment tokens, Runner configuration, or host secrets.
- Keep monitoring and administration runtimes private.
- Public marketplace presentation should use screenshots or media-only previews when the live runtime contains private infrastructure information.

## Testing

- Parse host files from fixture directories by overriding a trusted backend host-root setting.
- Mock Docker responses or the Docker client.
- Test missing and malformed files.
- Test that restricted Docker fields never appear in response DTOs.
- Test that command execution uses fixed argument arrays, `shell=False`, and timeouts when subprocesses are unavoidable.
- Test the App on a Standard Runner or without host mounts to confirm graceful degradation.
