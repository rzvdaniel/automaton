# Automaton CLI NuGet Publishing and Installation

This document explains:

- how to publish the Automaton CLI to NuGet.org using Azure Pipelines
- how to install the locally packed CLI globally for development testing
- how end users should install the CLI globally after it is published

## Relevant Files

- Pipeline: `azure-pipelines-cli.yml`
- CLI project: `src/Automaton.Cli/Automaton.Cli.csproj`

## 1. Publish the CLI to NuGet.org

The repository already contains an Azure DevOps pipeline for the CLI.

Pipeline file:

```yaml
azure-pipelines-cli.yml
```

### What the pipeline does

On changes to the CLI project or pipeline file, it:

1. restores the CLI project
2. builds it in `Release`
3. packs it as a .NET global tool NuGet package
4. publishes the package artifact in Azure DevOps
5. on `main` only, pushes the `.nupkg` to NuGet.org

### Requirements

Before the first real publish, make sure all of these are in place:

1. A NuGet.org account
2. Access to create an API key on NuGet.org
3. An Azure DevOps pipeline created from `azure-pipelines-cli.yml`
4. A secret Azure DevOps pipeline variable named `NUGET_ORG_API_KEY`

### Create the NuGet API key

1. Sign in to NuGet.org
2. Go to your account settings
3. Create an API key with package push permissions
4. Copy the API key once and store it safely

### Add the API key to Azure DevOps

In the pipeline settings or variables UI:

1. Create a variable named `NUGET_ORG_API_KEY`
2. Mark it as secret

The pipeline already expects that exact name.

### Safe dry run

Run the pipeline on `develop` first.

With the current pipeline logic:

- `BuildAndPackage` runs on `develop`
- `Release` is skipped on `develop`
- no package is pushed to NuGet.org

This is the recommended first validation step.

### Real publish

To publish for real:

1. merge the CLI changes into `main`
2. run the pipeline on `main`
3. confirm the package is packed successfully
4. let the `Release` stage push it to NuGet.org

### Versioning in Azure Pipelines

The pipeline uses the Azure DevOps build number as the NuGet version.

Current pipeline naming pattern:

```yaml
name: 1.0.$(Date:yyyyMMdd)$(Rev:.r)
```

And the pack step uses:

```yaml
versioningScheme: 'byBuildNumber'
```

That means pipeline-produced versions look like this:

```text
1.0.20260505.1
1.0.20260505.2
```

This avoids manually editing the version before each publish.

## 2. Install the Locally Packed CLI Globally for Development

Before publishing to NuGet.org, you can test the exact package locally.

### Local project config vs global config

`automaton config` now writes project-local CLI settings by default:

```text
./.automaton/config.json
```

Example:

```powershell
automaton config --server https://api.automaton.monster --key ak_execute_xxx
```

If you want shared machine-wide settings instead, use:

```powershell
automaton config --global --server https://api.automaton.monster --key ak_execute_xxx
```

The global path remains:

```text
~/.automaton/config.json
```

### Fast local update workflow when you changed the CLI

Local `dotnet pack` now stamps the CLI package with a UTC timestamp version by default. A local pack will produce versions like:

```text
1.0.20260523.170512
```

That makes local `dotnet tool update` deterministic without manually editing the project version for day-to-day testing.

If you need a specific release version, pass it explicitly with `-p:Version=...` during pack.

Then run this from the repository root:

```powershell
dotnet pack src/Automaton.Cli/Automaton.Cli.csproj --configuration Release --output .tmp/cli-pack
dotnet tool update --global --add-source .\.tmp\cli-pack Automaton.Cli --version <packed-version>
automaton --version
```

Exact update command:

```powershell
dotnet tool update --global --add-source .\.tmp\cli-pack Automaton.Cli --version <packed-version>
```

Why this matters:

- using the freshly packed timestamp version makes the update deterministic
- `automaton --version` is the fastest check that the refreshed binary is actually running

### One-command local reinstall script

For day-to-day local development, you can use:

```powershell
.\scripts\dev-reinstall-cli.ps1
```

What it does:

1. runs the CLI test project
2. packs `src/Automaton.Cli/Automaton.Cli.csproj`
3. detects the exact version of the freshly packed local `.nupkg`
4. builds a temporary NuGet config that points only at the local package folder
5. uninstalls the existing global `Automaton.Cli` tool
6. installs the freshly packed local tool from the local source only
7. prints `automaton --version`

The temporary local-only NuGet config matters because `--add-source` alone does not guarantee that `dotnet tool install` will prefer your local package over a higher version from another configured NuGet source.

Optional flags:

```powershell
.\scripts\dev-reinstall-cli.ps1 -SkipTests
.\scripts\dev-reinstall-cli.ps1 -Configuration Debug
```

If PowerShell blocks the script, run:

```powershell
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope Process
```

### Pack the CLI locally

From the repository root:

```powershell
dotnet pack src/Automaton.Cli/Automaton.Cli.csproj --configuration Release --output .tmp/cli-pack
```

This should create a file like:

```text
.tmp/cli-pack/Automaton.Cli.1.0.20260523.170512.nupkg
```

### Install it globally from the local package source

```powershell
dotnet tool install --global --add-source .\.tmp\cli-pack Automaton.Cli
```

If it is already installed globally, update it instead:

```powershell
dotnet tool update --global --add-source .\.tmp\cli-pack Automaton.Cli
```

If another configured NuGet source still wins, uninstall/install with the local-only reinstall script is more reliable than a plain update:

```powershell
dotnet tool uninstall --global Automaton.Cli
dotnet tool install --global --add-source .\.tmp\cli-pack Automaton.Cli
```

### Verify the install

```powershell
automaton --help
automaton init --help
automaton link --help
automaton deploy --help
```

### Remove the global install if needed

```powershell
dotnet tool uninstall --global Automaton.Cli
```

## 3. How End Users Should Install the CLI Globally

Once the package is published to NuGet.org, end users should use the normal .NET global tool install flow.

### First-time install

```powershell
dotnet tool install --global Automaton.Cli
```

### Update to the latest published version

```powershell
dotnet tool update --global Automaton.Cli
```

### Verify the install

```powershell
automaton --help
```

### Requirements for users

Users need a compatible .NET SDK/runtime environment that supports .NET global tools.

If `automaton` is not found after install, they should:

1. open a new terminal
2. verify that the .NET global tools path is on `PATH`
3. run `dotnet tool list --global`

## 4. Recommended Release Flow

Use this sequence for reliable releases:

1. test locally with `dotnet pack`
2. install the local package globally with `--add-source .\.tmp\cli-pack`
3. verify key commands locally
4. run the Azure pipeline on `develop`
5. confirm artifact generation succeeded
6. merge to `main`
7. let Azure Pipelines publish to NuGet.org
8. verify public install with `dotnet tool install --global Automaton.Cli`

## 5. Quick Command Reference

### Local deploy behavior notes

`automaton deploy` now uses this resolution order for the server URL:

1. `AUTOMATON_SERVER`
2. `server` in `automaton.json`
3. `./.automaton/config.json`
4. `~/.automaton/config.json`
5. interactive prompt

That means a project can be self-contained when `automaton.json` includes:

```json
{
	"appId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
	"name": "My App",
	"server": "https://api.automaton.monster"
}
```

`automaton deploy` also deploys backend scripts using the same API key auth path as the frontend upload. If the app has no `backend/` scripts, the expected result is:

```text
No backend scripts found
```

### Deploy troubleshooting

- If the CLI still prompts for a server URL, check whether the installed global tool is stale and reinstall or update it from the latest local package.
- If you expected project-local credentials to be used, check `./.automaton/config.json` in the app folder you are deploying from.
- If `dotnet tool update` still installs an older package, install the exact packed version or use `.\scripts\dev-reinstall-cli.ps1` so only the local feed is considered.
- If deploy uploads successfully but backend deploy returns `401`, verify the API key and make sure the local server is running the latest backend build.

### Local pack

```powershell
dotnet pack src/Automaton.Cli/Automaton.Cli.csproj --configuration Release --output .tmp/cli-pack
```

### Local global install from packed package

```powershell
dotnet tool install --global --add-source .\.tmp\cli-pack Automaton.Cli
```

### Local global update from packed package

```powershell
dotnet tool update --global --add-source .\.tmp\cli-pack Automaton.Cli
```

### Public install after NuGet publish

```powershell
dotnet tool install --global Automaton.Cli
```

### Public update after NuGet publish

```powershell
dotnet tool update --global Automaton.Cli
```
