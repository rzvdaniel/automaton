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

### Pack the CLI locally

From the repository root:

```powershell
dotnet pack src/Automaton.Cli/Automaton.Cli.csproj --configuration Release --output .tmp/cli-pack
```

This should create a file like:

```text
.tmp/cli-pack/Automaton.Cli.1.0.0.nupkg
```

### Install it globally from the local package source

```powershell
dotnet tool install --global --add-source .\.tmp\cli-pack Automaton.Cli
```

If it is already installed globally, update it instead:

```powershell
dotnet tool update --global --add-source .\.tmp\cli-pack Automaton.Cli
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