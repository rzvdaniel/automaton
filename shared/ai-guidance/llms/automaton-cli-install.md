# Automaton CLI Install and Setup

Use this when the user wants to install the Automaton CLI, verify that it is available, or prepare the current machine or workspace before app work begins.

## Goal

Make sure the `automaton` command is available, authenticated, and ready for local app work.

## Install

Preferred managed install:

```bash
dotnet tool install -g Automaton.Cli
```

Verify the install:

```bash
automaton --version
```

If the user prefers platform-specific installers or portable binaries, use the official Automaton CLI docs or downloads page instead of inventing a custom install path.

## First-time setup

Preferred:

```bash
automaton setup
```

That should authenticate the current project or workspace and sync the local generated AI guidance pack.

Fallback when the user wants explicit API key configuration:

```bash
automaton config --key ak_execute_xxx
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
4. Use the CLI deploy workflow guide for validate, runner, deploy, and publish behavior.
