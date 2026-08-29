# Generic runtime Stage 6: product surface

Stage 6 makes the CLI and Studio describe applications through the canonical
stack-neutral application contract. It does not add another build or runtime
implementation; those responsibilities remain in the Runner layers delivered
by Stages 2 and 3.

## Canonical contract

`automaton.json` and the Studio Application settings edit one contract:

- `build.command`: structured executable, arguments, working directory,
  environment, and optional shell mode;
- `build.outputDirectory` and `build.timeoutSeconds`;
- `runtime.command` and `runtime.port`;
- optional `health.path`, `health.port`, and startup timeout;
- persistent `storage.dataDirectory` and storage mounts.

Commands may use any executable available to the managed workspace. The
contract does not name a language, framework, package manager, or source
directory. Relative paths are validated by the shared Core contract validator.

The CLI validates and packages this contract. For a normal remote deployment it
does not execute the application build locally; the assigned Runner executes the
declared build once and then applies the resulting artifact through the generic
runtime lifecycle.

## Studio

The App Settings > Application panel edits the same build, runtime, readiness,
environment, and persistent-storage concepts that are serialized by the API.
Deployment status is presented at the application/runtime level. Studio does
not require a frontend directory, backend directory, Python entrypoint, Vue
project, or package-manager-specific file.

The existing controller/action editor and its Python-specific file operations
remain deferred compatibility functionality. They are not part of the generic
application contract and must not be treated as evidence that a generic runtime
exists.

## Templates and examples

Technology-specific templates may provide a convenient starting point, but a
template only writes the same generic contract. Infrastructure must never infer
execution behavior from a template name, file extension, source layout, or
dependency manifest.

For example, these are all valid descriptions when the corresponding tools are
available in the Runner workspace:

```json
{
  "build": {
    "command": { "executable": "node", "arguments": ["build.mjs"] },
    "outputDirectory": "dist"
  },
  "runtime": {
    "command": { "executable": "node", "arguments": ["server.mjs"] },
    "port": 8080
  }
}
```

```json
{
  "build": {
    "command": { "executable": "dotnet", "arguments": ["publish", "-o", "out"] },
    "outputDirectory": "out"
  },
  "runtime": {
    "command": { "executable": "./MyApp", "workingDirectory": "out" },
    "port": 8080
  },
  "health": { "path": "/health" }
}
```

These examples illustrate the shape only. They are not technology detection
rules or required project layouts.

## Scope boundary

Stage 6 covers the CLI and Studio product surface, validation messaging,
templates/examples, and user guidance. It intentionally does not implement a
new executor, redesign the editor, remove the controller/FastAPI bridge, or
provide the cross-stack acceptance matrix planned for Stage 7.
