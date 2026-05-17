# Automaton CLI Install and Setup

Use this when the user wants to install the Automaton CLI, verify that it is available, or prepare the current machine or workspace before app work begins.

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

## First-time setup

Preferred:

```bash
automaton setup
```

That should authenticate the current project or workspace and sync the local generated AI guidance pack.

Advanced fallback when the user explicitly wants to manage low-level CLI settings:

```bash
automaton setup --key ak_execute_xxx
```

That command saves project-local CLI settings in `./.automaton/config.json` by default. Use `automaton config --global ...` only when the user explicitly wants shared machine-wide settings.

## Clean auth reset

For testing, account switching, or a clean auth reset:

```bash
automaton logout
automaton logout --global
automaton logout --all
automaton setup
```

## What setup generates

`automaton setup`, `automaton new`, and `automaton link` sync local guidance into:

- `.automaton/agents/`
- `.automaton/ai-guidance/`
- compatibility wrappers like `AGENTS.md`, `CLAUDE.md`, and `.github/`

If those generated files are missing or stale, run:

```bash
automaton sync-ai
```

## Handoff to deploy workflow

After install and setup:

1. Keep working in the current folder.
2. Use `automaton new "<app-name>"` for a new app or `automaton link <app-id>` for an existing one.
3. Follow the local generated guidance first.
4. Use the CLI deploy workflow guide for deploy and publish behavior.
