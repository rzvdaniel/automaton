# Automaton CLI Install and Login

Use this when the user wants to install the Automaton CLI, verify that it is available, or authenticate the current machine before app work begins.

## Goal

Make sure the `automaton` command is available, authenticated, and ready for local app work.

## Install

Preferred managed install when .NET 9 is available:

```bash
dotnet tool install -g Automaton.Cli
```

Verify the install:

```bash
automaton --version
```

If `.NET 9` is already available, prefer the global tool because it is the easiest path for AI tools to install, update, and verify with `automaton --version`.

If `.NET 9` is not available or the environment cannot use global tools, use the official portable downloads or install scripts instead of inventing a custom install path.

Install decision rule:

1. Check whether `automaton` is already available.
2. If not, check whether `dotnet` is available.
3. If `dotnet` is available, prefer `dotnet tool install -g Automaton.Cli`.
4. If `dotnet` is not available, use the official platform download or installer script.
5. Always verify with `automaton --version` before proceeding.

## First-time login

Preferred:

```bash
automaton login
```

When prompted, the user should paste their Automaton API key. Do not ask the user to paste the key into chat unless there is no safer terminal prompt available.

`automaton login` authenticates the CLI. It should not scaffold app files.

Advanced fallback when the user explicitly wants to pass the key non-interactively:

```bash
automaton login --key ak_execute_xxx
```

## App creation handoff

After login, use the app workflow:

```bash
automaton new "My App"
automaton deploy
```

`automaton new` is responsible for creating the local app scaffold, `automaton.json`, `.automatonignore`, and the local AI guidance pack.

Use `automaton link <app-id>` only when the folder should attach to an existing Automaton app.

Do not interpret `automaton login` by itself as meaning the current folder is already an app.

## Clean auth reset

For testing, account switching, or a clean auth reset:

```bash
automaton logout
automaton logout --global
automaton logout --all
automaton login
```

## What app creation generates

`automaton new` and `automaton link` sync local guidance into:

- `.automaton/agents/`
- `.automaton/agents/references/`
- `AGENTS.md` as the only default root adapter

If those generated files are missing or stale, run:

```bash
automaton sync-ai
```

If you use an AI tool that needs its own discovery file, generate a tiny optional adapter:

```bash
automaton sync-ai --adapter claude
automaton sync-ai --adapter copilot
automaton sync-ai --adapter github-skills
automaton sync-ai --adapter all
```

## Handoff to deploy workflow

After install and login:

1. Keep working in the current folder.
2. Use `automaton new "<app-name>"` for a new app or `automaton link <app-id>` for an existing one.
3. Follow the local generated guidance first.
4. Use the CLI deploy workflow guide for deploy and publish behavior.
