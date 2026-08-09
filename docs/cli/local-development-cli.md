# Local Development CLI

This guide explains how to run the local `Automaton.Cli` project from an app folder without replacing the globally installed NuGet tool.

Environment note:

- Ordinary usage should target live by default.
- Named project environments are for local, staging, or other non-live targets.
- `AUTOMATON_SERVER` and `AUTOMATON_API_KEY` still override stored config when you intentionally need a one-off override.

## Goal

Use:

- the local unpublished CLI code from `C:\Work\automaton\src\Automaton.Cli`
- the app-local `automaton.json`
- the app-local `.automaton\environments\*.json` files
- the app-local `.automaton\config.json` only as a fallback credential store

This is useful when testing CLI changes before pushing a package to NuGet.

## Recommended PowerShell helpers

Add these functions to your active PowerShell profile:

```powershell
function automaton-dev {
    dotnet run --project C:\Work\automaton\src\Automaton.Cli\Automaton.Cli.csproj -- @args
}

function automaton-watch {
    dotnet watch run --project C:\Work\automaton\src\Automaton.Cli\Automaton.Cli.csproj -- @args
}
```

Recommended usage:

- `automaton-dev` for normal CLI testing
- `automaton-watch` while editing the CLI and re-running commands frequently

`automaton-watch` is noisier because it adds watch/restart output. For most command testing, `automaton-dev` is cleaner.

## Find the right PowerShell profile

PowerShell can use different profile files depending on the host. Do not assume a profile path. Check the current session:

```powershell
$PROFILE
$PROFILE | Test-Path
```

If the second command returns `False`, create the parent folder and file for the active profile:

```powershell
New-Item -ItemType Directory -Force -Path (Split-Path $PROFILE)
if (-not (Test-Path $PROFILE)) { New-Item -ItemType File -Path $PROFILE -Force }
notepad.exe $PROFILE
```

Paste the helper functions into that file, save it, then reload the profile:

```powershell
. $PROFILE
Get-Command automaton-dev
```

If `Get-Command automaton-dev` succeeds, the function is loaded in the current session.

## Why this matters

If you edit the wrong profile file, the function will not exist in the shell you are actually using.

This commonly happens when one host uses a profile such as:

```text
C:\Users\<user>\Documents\PowerShell\Microsoft.VSCode_profile.ps1
```

while another host uses:

```text
C:\Users\<user>\Documents\WindowsPowerShell\Microsoft.PowerShell_profile.ps1
```

Always trust the value of `$PROFILE` in the current session.

## Testing the local CLI from an app folder

From the app folder:

```powershell
automaton-dev config --show
automaton-dev validate
automaton-dev deploy
automaton-dev target list
automaton-dev deploy server-eu
```

`automaton deploy` deploys the only deployable environment immediately. When several
environments are available, interactive terminals show an environment picker; CI and other
non-interactive runs fail closed and print explicit `automaton deploy <environment-slug>`
commands. The Live environment is used when no explicit environment is needed.
`automaton deploy <environment-slug>` resolves a named environment. Environment definitions
and Runner assignments are not stored in `automaton.json`; manage them with
`automaton target list|add|remove|primary`. The hidden `target default` alias is
retained only for compatibility. See
[Environments](../apps/deployment-targets.md).

## Named project environments

For apps that need both live and local targets, prefer named project environments over custom shell helpers.

Example files:

```text
my-app/
  automaton.json
  .automaton/
    environment.json
    environments/
      live.json
      local.json
```

`automaton.json` should describe the app itself, not the deployment target:

```json
{
  "name": "Blog",
  "frontend": {
    "build": "cd frontend && npm install && npm run build",
    "deployDir": "frontend/dist"
  },
  "backend": {
    "deployDir": "backend"
  }
}
```

Example environment config:

```json
// .automaton/environments/live.json
{
  "server": "https://api.automaton.monster",
  "apiKey": "ak_live_..."
}
```

```json
// .automaton/environments/local.json
{
  "server": "https://api.automaton.test:8443",
  "apiKey": "ak_local_..."
}
```

Optional explicit default environment:

```json
// .automaton/environment.json
{
  "default": "live"
}
```

Use it like this:

```powershell
automaton-dev whoami
automaton-dev deploy
automaton-dev whoami --env local
automaton-dev status --env local
automaton-dev deploy --env local
automaton-dev deploy --env live
```

Currently supported on the main local/live workflow commands:

- `whoami --env <name>`
- `status --env <name>`
- `deploy --env <name>`
- `open --env <name>`
- `pull --env <name>`

When `--env` is omitted, the CLI uses this order:

1. `.automaton/environment.json` `default`
2. `.automaton/environments/live.json` if it exists
3. fallback credential resolution for commands that do not require a linked environment file

For `automaton new`, `automaton create`, and `automaton link`, omitting `--env` creates or updates the `live` environment automatically. For `automaton pull <app-id-or-slug>`, the selected environment is retained in the newly linked project; use `--env <name>` to choose it explicitly.

For deploy, the CLI prints the resolved target before making changes. Example:

```text
Deploying app Automaton Blog
Environment: local
Server: https://api.automaton.test:8443
```

This runs the local development CLI, but uses the current working directory as the app root.

That means it will resolve:

- `automaton.json` from the app folder
- `.automaton\environments\<name>.json` when an environment is selected
- `.automaton\config.json` from the app folder when present
- global config only as a fallback

Example:

```powershell
cd C:\Work\automaton-apps\blog
automaton-dev config --show
automaton-dev deploy
```

To force a local server for the current shell, even if `automaton.json`, `.\.automaton\config.json`, or `%USERPROFILE%\.automaton\config.json` point somewhere else:

```powershell
$env:AUTOMATON_SERVER="http://localhost:5091"
$env:AUTOMATON_API_KEY="<your-local-api-key>"
automaton-dev whoami
automaton-dev deploy
```

## Which server and API key are used

For most commands, the CLI resolves credentials in this order:

Server:

1. `AUTOMATON_SERVER`
2. selected named environment `.automaton\environments\<name>.json`
3. nearest `.automaton\config.json`
4. global `%USERPROFILE%\.automaton\config.json`
5. prompt

API key:

1. `AUTOMATON_API_KEY`
2. selected named environment `.automaton\environments\<name>.json`
3. nearest `.automaton\config.json`
4. global `%USERPROFILE%\.automaton\config.json`
5. prompt

App ID:

1. selected named environment `.automaton\environments\<name>.json`

Important exception:

- `automaton login` defaults the server to `https://api.automaton.monster` when `--server` is not provided

To confirm exactly what a specific app folder will use:

```powershell
automaton-dev config --show
```

## What `automaton login` writes

By default, a successful `automaton login` saves global machine credentials to:

```text
%USERPROFILE%\.automaton\config.json
```

Use `--local` when the credential should apply only to the current project:

```text
<app-root>\.automaton\config.json
```

This does not conflict with:

- `automaton.json` in the app root
- `.automaton\environments\...`
- `.automaton\agents\...` harness files

Typical layout:

```text
my-app/
  automaton.json
  .automaton/
    config.json
    environments/
    agents/
```

## When to use `dotnet pack` instead

Use `automaton-dev` while developing behavior.

Use `dotnet pack` and install/update the packed tool from a local source when you need to verify:

- the packaged tool behavior
- install/update behavior
- versioning and NuGet packaging issues

For that workflow, see:

- [docs/prod/cli-nuget-publishing.md](../prod/cli-nuget-publishing.md)
