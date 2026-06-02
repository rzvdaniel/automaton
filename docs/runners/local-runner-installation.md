# Local Runner Installation

Automaton CLI can install a local runner for you in one command.

Recommended:

```bash
automaton runner install
```

By default this installs the Docker runner. Native install remains available as an explicit power-user option:

```bash
automaton runner install --native
```

## Requirements

- Automaton CLI configured with `automaton setup`
- Docker Desktop / Docker Engine for the default install path
- Python 3.11+ only if you use `--native` and want backend Python apps to run on this machine

The runner itself uses the CLI's stored credentials when possible, so in the common case you only need to provide a runner name.

The service/runtime receives:

- `AUTOMATON_SERVER_URL`
- `AUTOMATON_API_KEY`
- `AUTOMATON_RUNNER_NAME`

## Docker Install

```bash
automaton runner install
```

The CLI will:

1. Prompt for a runner name
2. Reuse the server URL and API key already configured in the CLI
3. Validate Docker
4. Pull the configured runner image
5. Create a named volume
6. Start the container with the CLI's stored server URL and API key

Environment override for private or alternate images:

```bash
AUTOMATON_RUNNER_IMAGE=your-registry/runner:latest
```

Current public default:

```bash
rzvdaniel/automaton-runner:latest
```

This is the recommended path for backend app execution because it gives you a known Python/runtime environment with fewer host-machine issues.

## Native Install

```bash
automaton runner install --native
```

The CLI will:

1. Prompt for a runner name
2. Reuse the server URL and API key already configured in the CLI
3. Download the latest native runner archive
4. Extract it under `~/.automaton/runner/installs/<runner-name>/`
5. Create a platform-native service
6. Start the service

Platform service managers:

- Windows: Windows Service
- Linux: `systemd`
- macOS: `launchd`

Use this when you intentionally want the runner to access the host machine directly.

Advanced manual native install is also available as a .NET tool:

```bash
dotnet tool install -g Automaton.Runner
```

Then configure:

- `AUTOMATON_SERVER_URL`
- `AUTOMATON_API_KEY`
- `AUTOMATON_RUNNER_NAME`

and start it with:

```bash
automaton-runner
```

## Runner Management

```bash
automaton runner status
automaton runner logs
automaton runner stop
automaton runner restart
automaton runner uninstall
```

Useful options:

```bash
automaton runner status --name my-laptop
automaton runner uninstall --delete-data
automaton runner install --replace
automaton runner install --native --replace
```

## Troubleshooting

### Docker not running

If `automaton runner install` fails with a Docker check error:

- Install Docker Desktop or Docker Engine
- Start the Docker daemon
- Retry the command

### Python not installed

Native install can still succeed without Python, but backend Python apps will not run until Python 3.11+ is installed.

Check:

```bash
python --version
```

### Runner does not appear online

Check logs:

```bash
automaton runner logs
```

Then verify:

- `AUTOMATON_SERVER_URL` points to the correct API host
- `AUTOMATON_API_KEY` is valid and has runner access
- outbound network / firewall rules allow the runner to reach the Automaton API

### Runner install download fails

The native installer downloads a prebuilt archive. If the URL returns `404` or an old version:

- verify the latest runner artifacts were published to `/downloads/runner/latest/`
- do not expect `prod-redeploy-website.sh` or `prod-redeploy-all.sh` to refresh those files
- republish runner download artifacts separately from the website deploy flow

If you only need a native runner and do not want to rely on archive hosting, prefer:

```bash
dotnet tool install -g Automaton.Runner
```

### Remove the runner

Keep local data:

```bash
automaton runner uninstall
```

Remove local data too:

```bash
automaton runner uninstall --delete-data
```
