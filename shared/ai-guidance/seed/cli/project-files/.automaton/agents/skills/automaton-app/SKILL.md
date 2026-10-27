# Automaton app workflow

Use this workflow for any app in this folder.

The app contract is the source of truth for build and runtime behavior. Inspect
the existing files before choosing commands, paths, or dependencies.

1. Inspect `automaton.json` and the current app link.
2. If `automaton.json` is missing, run `automaton new "<name>"` for a new
   remote app or `automaton link <app-id>` for an existing remote app.
3. If `automaton.json` exists but is not linked in the current environment, run
   `automaton create` for a new remote app for this scaffold or
   `automaton link <app-id>` for an existing remote app.
4. If the project is linked, continue with the normal workflow.
5. Understand the requested application.
6. Choose any suitable technology and source layout.
7. Implement the app.
8. Fill the `application` fields in `automaton.json` for build, runtime,
   health, and storage.
9. Run `automaton validate` and fix its errors.
10. Run `automaton deploy`.
11. Wait for the asynchronous operation to reach a terminal result.
12. If it reaches `Succeeded`, verify the deployed URL.
13. If it fails, inspect the exact failure and fix it.
14. Publish when it is within the requested app delivery or an existing standing authorization. Do not ask for separate publication approval again; honor any user restriction to keep the app private or unpublished. For authorized non-interactive publication, use `automaton publish --yes` and verify the published URL.

Use an assigned Machine Management Runner for new self-hosted applications. Do not use a
Privileged Host Runner as an automatic fallback. Privileged Host approval is
persisted per app environment: after explicit user approval, run
`automaton target approve <environment>` and use
`automaton target revoke <environment>` to remove it. If only a Privileged
Host Runner appears usable, stop and ask the user for explicit approval.

Dynamic HTTP apps must declare `application.runtime.command` and an explicit
runtime image. Static sites use a build command and `build.outputDirectory`,
with no runtime command or Compose contract; the Machine Management Runner
publishes the declared output through its shared static server. Actions-only
or other finite-work apps with no HTTP endpoint may also omit the runtime command. Make the process,
port, health path, and data directory explicit whenever the app has a runtime.
Keep generated output and dependencies out of durable source unless the user
specifically asks for them.

Automaton does not require a language, framework, package manager, source
layout, or application architecture. The remote Runner performs the configured
build. The app must listen on the effective port supplied through
`AUTOMATON_PORT`. Put durable data under `AUTOMATON_APP_DATA_DIR`; build and
runtime outputs are disposable. Prefer structured commands and use shell mode
only when required.

Use the smallest contract that describes the app. Do not invent a directory
convention, framework, or service shape to satisfy this guidance. Preserve
existing app metadata and unrelated user files while editing.

```json
{
  "application": {
    "build": {
      "image": "build-image:tag",
      "command": {
        "executable": "build-tool",
        "arguments": ["build"],
        "workingDirectory": ".",
        "environment": {}
      },
      "outputDirectory": "dist",
      "timeoutSeconds": 900
    },
    "runtime": {
      "image": "runtime-image:tag",
      "command": {
        "executable": "app-server",
        "arguments": [],
        "workingDirectory": ".",
        "environment": {}
      },
      "port": 8080
    },
    "health": { "path": "/health", "startupTimeoutSeconds": 60 },
    "storage": { "dataDirectory": "data", "mounts": [] }
  }
}
```

The runtime image is explicit and is not inferred from the executable or source
files. For illustration only, a project may use `node:22-bookworm-slim` with
`node server.js`, `python:3.13-slim` with `python app.py`, or an ASP.NET runtime
image with its published application command. Choose the image and command for
the application; Automaton does not detect or select a stack.

## Efficient image selection

Preserve the user's chosen technology. Prefer the smallest maintained image
from an official or trusted publisher that supports the app's actual dependencies
and the Runner architecture. Image size is a preference, not a reason to remove
required libraries, security updates, health checks, or durable storage.

- For static output, use the static-site contract in this skill so the Runner shares its
  static serving container across sites. Do not add a dedicated nginx, Node, or
  Python HTTP container for each static app. Server-rendered apps and APIs still
  need their declared runtime process.
- Separate `build.image` from `runtime.image` when build tools are required only
  during compilation or packaging. Keep SDKs, compilers, development dependencies,
  and build caches out of the deployed runtime artifact. For a custom Dockerfile,
  use a multi-stage build when appropriate.
- For Python, consider an official `python:<supported-version>-slim` image when
  its libraries suffice. For Node, consider a supported slim variant and retain
  only production dependencies. For .NET, build with an SDK image and run with
  the matching ASP.NET or .NET runtime image; use runtime-deps only when the
  published app includes its runtime. These are examples, not required stacks.
- Use Alpine or other minimal variants only after checking native-library,
  certificate, globalization, and executable compatibility. Do not assume every
  app can run on Alpine or in an image without a shell.
- Reuse compatible image versions across apps where practical so Docker can
  share cached layers. Declare explicit supported version tags or digests rather
  than `latest`, and plan updates for security fixes.
- Use a supported database image when a database is needed, and keep its durable
  data in configured persistent storage. Do not replace a required database just
  to shrink the image; its growing data, backups, and memory also need capacity.

Validate and deploy the chosen image through the normal CLI flow and verify
health and application behavior. When reporting resource use, distinguish shared
image layers, container writable layers, app files and retained deployments,
persistent data, logs, and RAM; image size alone is not per-app capacity.
