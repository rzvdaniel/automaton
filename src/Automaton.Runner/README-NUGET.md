# Automaton Runner

Automaton Runner is the native machine runner for Automaton.

Most users should prefer the Docker runner installed through:

```bash
automaton runner install
```

The native runner is intended for advanced scenarios where the runner needs direct access to the host machine, local files, or local processes.

Typical install:

```bash
dotnet tool install -g Automaton.Runner
```

After installation, configure:

- `AUTOMATON_API_KEY`
- `AUTOMATON_RUNNER_NAME`

Optional:

- `AUTOMATON_SERVER_URL`

`AUTOMATON_SERVER_URL` is only needed when you want to point the runner at a non-default Automaton API endpoint for development or self-hosting. The hosted default should target `https://api.automaton.monster`.

Then run:

```bash
automaton-runner
```
