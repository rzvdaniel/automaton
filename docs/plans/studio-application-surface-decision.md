# Studio Application Surface Decision

Date: 2026-06-27

## Decision

Automaton Studio uses one app model:

- `Application` is the currently deployed running app.
- `Files` are the editable source that will be deployed next.
- `Deploy` is the only operation that moves source into runtime.

This replaces the preview-centric mental model for the primary Studio UX.

## Product Rules

- Rename the main Studio app surface from `Preview` to `Application`.
- `Application` always shows the currently deployed restricted runtime.
- `Files` remain the editable source surface.
- Source edits do not change the running application until deploy succeeds.
- Restricted and public are access policies, not separate preview concepts.
- Public publishing behavior does not change in this phase.

## User-Facing Model

- `Application`
- `Files`
- `AI Chat`
- `Deployments`
- `Logs`
- `Settings`

Behavior:

- `Application` shows what is running now.
- `Files` show what will be deployed next.
- `Deploy` synchronizes `Files -> Application`.
- `Open` opens the same restricted runtime outside Studio.

## Why

This removes multiple confusing concepts at once:

- no separate source preview concept
- no ambiguity between edited state and running state
- CLI deploy and Studio deploy follow the same model
- the first app tab becomes an application control panel instead of an editor-render hybrid

## First-Cut Implementation

1. Replace the current source-preview pane/AppPreviewController usage in the main app pane with the restricted runtime URL used by `automaton open`.
2. Rename `Preview` UI labels to `Application` or `Running Application`.
3. Keep `Files` as the editable source editor.
4. Show an `undeployed changes` indicator when source changed after the last successful deployment.
5. Show initial deployment/runtime states where they can be derived reliably:
   - `Deployed`
   - `Source changed`
   - `Build failed`
   - `Runner offline`
6. Do not change public publishing behavior yet.
7. Do not remove old preview backend code until the new `Application` surface works.

## Follow-Up

- turn the `Application` tab into an explicit app control panel
- refine runtime/deploy state modeling
- retire source-preview-only backend code after no Studio surface depends on it
