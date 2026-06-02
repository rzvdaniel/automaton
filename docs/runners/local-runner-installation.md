# Local Runner Installation

Automaton CLI can install a local runner for you in one command.

Recommended:

```bash
automaton runner install
```

By default this installs a native runner service on the current machine. Docker remains available as an opt-in mode:

```bash
automaton runner install --docker
```

## Requirements

- Automaton CLI configured with `automaton setup`
- Docker Desktop / Docker Engine only if using `--docker`
- Python 3.11+ only if you want backend Python apps to run on this machine

The runner itself uses the CLI's stored credentials when possible, so in the common case you only need to provide a runner name.

The service/runtime receives:

- `AUTOMATON_SERVER_URL`
- `AUTOMATON_API_KEY`
- `AUTOMATON_RUNNER_NAME`

## Native Install

```bash
automaton runner install
```

The CLI will:

1. Prompt for a runner name
2. Reuse the server URL and API key already configured in the CLI
3. Download the latest native runner archive
4. Extract it under `~/.automaton/runner/installs/<runner-name>/`
5. Create a platform-native service
6. Start the service

Current download source:

- the CLI expects the latest native runner archives at `https://studio.automaton.monster/downloads/runner/latest/`
- those archives are built separately from the production website deploy flow
- if the download endpoint is missing or stale, publish fresh runner artifacts before asking users to rely on `automaton runner install`

Platform service managers:

- Windows: Windows Service
- Linux: `systemd`
- macOS: `launchd`

## Docker Install

```bash
automaton runner install --docker
```

The CLI will:

1. Validate Docker
2. Pull the configured runner image
3. Create a named volume
4. Start the container with the CLI's stored server URL and API key

Environment override for private or alternate images:

```bash
AUTOMATON_RUNNER_IMAGE=your-registry/runner:latest
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
automaton runner install --docker --replace
```

## Troubleshooting

### Docker not running

If `automaton runner install --docker` fails with a Docker check error:

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

### Remove the runner

Keep local data:

```bash
automaton runner uninstall
```

Remove local data too:

```bash
automaton runner uninstall --delete-data
```
