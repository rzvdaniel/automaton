# Automaton CLI + Codex E2E Testing Guide

## Overview

This guide tests the real new-user flow end-to-end:

- user discovers Automaton from the website
- user opens Codex in a fresh local folder
- user asks for a playable Tetris app
- Codex uses the Automaton CLI as the primary workflow
- the app is deployed and optionally published

This is a manual acceptance checklist for the AI-first app-creation experience.

## Goal

Verify that a new user can become productive with:

1. a fresh local folder
2. `automaton setup`
3. local generated guidance
4. `automaton new`
5. `automaton validate`
6. `automaton deploy`
7. explicit publish confirmation

## Prerequisites

- Automaton production or staging environment is available
- A valid test user account exists
- Codex is available locally
- Internet access is available for CLI install, auth, and deploy

Optional for a stricter first-time-user test:

- use a fresh machine, VM, or container
- or remove any prior local CLI auth state before testing

## Test Folder Setup

Use a fresh folder such as:

```powershell
mkdir C:\apps\tetris-e2e-test
cd C:\apps\tetris-e2e-test
```

If you want to simulate a fresh auth state:

```powershell
automaton logout
automaton logout --all
```

## Scenario 1: New User Builds a Tetris App

**Test:** Codex creates a playable frontend-only Tetris app in the current folder and deploys it with the Automaton CLI.

**Steps:**

1. Open Codex in `C:\apps\tetris-e2e-test`
2. Paste this prompt:

```text
Use the local generated Automaton guidance in this folder if it exists; otherwise read https://automaton.monster/llms.txt. Build a playable Tetris app for Automaton in this folder. Use the Automaton CLI as the primary execution path. Start with automaton setup. After that, handle the rest yourself: install the CLI if needed, create automaton.json here, scaffold the app, validate, deploy, and ask before publish. Only ask me to step in for auth or publish approval.
```

3. Observe the AI workflow from setup through deploy
4. Complete login or API key auth only if the tool asks for it
5. Approve publish only if you want to test the publish step too

**Expected Results:**

- ✅ Codex checks whether `automaton` is installed
- ✅ If missing, Codex installs the CLI or tells the user it is installing it
- ✅ Codex starts with `automaton setup`
- ✅ Codex asks the user to step in only for auth when needed
- ✅ `automaton setup` generates local guidance files in the current folder
- ✅ Codex follows the local generated guidance after setup
- ✅ Codex uses `automaton new` for a new app
- ✅ `automaton.json` is created in the current folder, not inside an unexpected nested folder
- ✅ `.automatonignore` is created when needed
- ✅ The generated app is frontend-only for Tetris unless backend logic is explicitly justified
- ✅ Codex runs `automaton validate` before deploy
- ✅ Codex does not require runner setup for a frontend-only Tetris app
- ✅ Codex runs `automaton deploy`
- ✅ The app is previewable in Automaton Studio after deploy
- ✅ Codex asks for confirmation before publish
- ✅ If approved, Codex runs `automaton publish --yes`

## Files Expected After Setup

After `automaton setup`:

- `AGENTS.md`
- `CLAUDE.md`
- `.automaton/agents/`
- `.automaton/ai-guidance/`
- `.github/copilot-instructions.md`
- `.github/skills/automaton-app/SKILL.md`

After `automaton new`:

- `automaton.json`
- `.automatonignore`
- local app source files

## What to Inspect Locally

Check that:

- `automaton.json` is in the current folder
- the app files are in the current folder
- no surprise nested app folder was created
- no unnecessary `backend/` folder was created for a simple Tetris app
- generated local guidance exists and points to `.automaton/agents/` as canonical

## What to Inspect in Automaton Studio

Check that:

- the app exists remotely
- deploy completed successfully
- preview opens
- the Tetris app is actually playable
- publish only happens after explicit approval

## Failure Conditions

Treat these as failures:

- ❌ Codex skips `automaton setup`
- ❌ Codex creates an app in a nested folder without being asked
- ❌ `automaton.json` is missing after create/link should have happened
- ❌ Codex keeps relying on website `llms.txt` after local guidance is generated
- ❌ Codex invents backend code or runner steps for a simple frontend-only Tetris app
- ❌ Codex deploys without validation
- ❌ Codex publishes without confirmation
- ❌ The final app is not playable in Studio

## Scenario 2: CLI Already Installed, Not Authenticated

**Test:** The same flow works when `automaton` is already installed but the user is not logged in.

**Expected Results:**

- ✅ Codex detects the CLI is present
- ✅ Codex still runs `automaton setup`
- ✅ Auth is requested only when needed
- ✅ The rest of the flow behaves the same as Scenario 1

## Scenario 3: Existing App Link Flow

**Test:** Codex links the current folder to an existing Automaton app instead of creating a new one.

**Prompt Variant:**

```text
Use the local generated Automaton guidance in this folder if it exists; otherwise read https://automaton.monster/llms.txt. I already have an Automaton app. Use the Automaton CLI as the primary execution path. Start with automaton setup, then link this folder to app id <app-id>, scaffold a playable Tetris app here, validate, deploy, and ask before publish.
```

**Expected Results:**

- ✅ Codex uses `automaton link <app-id>`
- ✅ `automaton.json` is created in the current folder
- ✅ The rest of the flow continues normally

## Scenario 4: Framework Variant

**Test:** Ask for a React or Vue Tetris app and verify build-based deploy behavior.

**Expected Results:**

- ✅ Codex uses the requested framework
- ✅ `automaton.json` includes `build` and `deployDir` when needed
- ✅ `automaton validate` succeeds before deploy
- ✅ `automaton deploy` uses the correct output folder

## Quick Smoke Test Checklist

- [ ] Fresh folder used
- [ ] Codex starts with `automaton setup`
- [ ] Local guidance files generated
- [ ] `automaton.json` created in current folder
- [ ] No unwanted nested app folder
- [ ] Tetris app scaffolded locally
- [ ] No unnecessary backend or runner flow for frontend-only app
- [ ] `automaton validate` run before deploy
- [ ] `automaton deploy` succeeded
- [ ] App preview works in Studio
- [ ] Publish required explicit approval
- [ ] If approved, publish succeeded

---

**Version:** 1.0.0  
**Last Updated:** May 9, 2026
