# Local Development CLI

This guide explains how to run the local `Automaton.Cli` project from an app folder without replacing the globally installed NuGet tool.

Environment note:

- `AUTOMATON_SERVER` and `AUTOMATON_API_KEY` now override stored project and global CLI config.
- This is the safest way to force commands to a local API during development without rewriting existing login state.

## Goal

Use:

- the local unpublished CLI code from `C:\Work\automaton\src\Automaton.Cli`
- the app-local `automaton.json`
- the app-local `.automaton\config.json` when present

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
```

This runs the local development CLI, but uses the current working directory as the app root.

That means it will resolve:

- `automaton.json` from the app folder
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
2. `automaton.json` `server`
3. nearest `.automaton\config.json`
4. global `%USERPROFILE%\.automaton\config.json`
5. prompt

API key:

1. `AUTOMATON_API_KEY`
2. nearest `.automaton\config.json`
3. global `%USERPROFILE%\.automaton\config.json`
4. prompt

Important exception:

- `automaton login` defaults the server to `https://api.automaton.monster` when `--server` is not provided

To confirm exactly what a specific app folder will use:

```powershell
automaton-dev config --show
```

## What `automaton login` writes

By default, a successful `automaton login` saves project-local credentials to:

```text
<app-root>\.automaton\config.json
```

If you pass `--global`, it writes to:

```text
%USERPROFILE%\.automaton\config.json
```

This does not conflict with:

- `automaton.json` in the app root
- `.automaton\agents\...` harness files

Typical layout:

```text
my-app/
  automaton.json
  .automaton/
    config.json
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
