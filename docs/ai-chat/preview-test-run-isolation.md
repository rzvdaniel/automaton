# Preview Test Run Isolation (`testRunId`) — How It Works

## Why this exists

When AI tests apps in the App Editor preview, we need a way to separate **test executions** from normal app usage and to trace what happened in one AI run.

The platform now propagates a `testRunId` across preview calls so backend logic can detect:  
"this request is part of AI test run X".

---

## End-to-end flow

### 1) Frontend creates one test run id per preview session

In `app-editor.store.ts`, the preview session gets a stable id:

- `previewTestRunId = "preview-{appId}-{timestamp}"`
- Reused across `refreshPreview()` calls in that session
- Reset when the editor store resets

This makes all calls from the same preview session traceable.

### 2) Preview script is injected with that id

`apps.service.ts` calls:

- `buildPreviewApiScript(appId, baseUrl, token, testRunId)`

`preview-api-script.ts` injects `__TEST_RUN_ID__` into:

- `scripts/automaton-api-preview.js`

So `window.automatonApi` in the iframe always knows the active test run id.

### 3) Every preview API call carries test run context

Inside `automaton-api-preview.js`, requests include both:

- Header: `X-Automaton-Test-Run-Id: <id>`
- Body field: `testRunId: "<id>"`

This is done for preview flow/controller calls (and can be extended for any future preview endpoints).

### 4) Backend resolves and injects context into execution inputs

Controllers now resolve test run id with this precedence:

1. `request.TestRunId` (body)
2. `X-Automaton-Test-Run-Id` (header)

Then they inject it into execution inputs as:

- `__testRunId`

This happens in:

- `AppsFlowExecutionController`
- `AppsControllerExecutionController`
- `AppsStepExecutionController`

Shared constants live in:

- `Constants/AppExecutionContextKeys.cs`

---

## New/updated API contract

These request DTOs now include optional `TestRunId`:

- `WebAppFlowExecutionRequest`
- `AppControllerActionRequest`
- `AppStepExecutionRequest`

`TestRunId` is optional to preserve backward compatibility.

---

## Preview flow endpoint support

Preview script was already calling:

- `POST /api/web-apps/preview/{appId}/flows/{flowIdentifier}/execute`

A dedicated preview flow endpoint now exists in `AppsFlowExecutionController` to support that route explicitly.

---

## How app/backend code should use it

Generated app backend logic can read the input key:

- `__testRunId`

Recommended behavior:

1. If `__testRunId` exists, write data to test/staging tables or test partition keys.
2. Tag created rows with `testRunId` for cleanup.
3. Mark AI claims as "persistence-verified" only after read-back assertions in same run id scope.

---

## Practical strategy for broad apps

For apps with many tables/entities, prefer a generic convention:

- Add nullable `TestRunId` column (or metadata field) in writable entities.
- On write, propagate `__testRunId` automatically in a shared repository/service layer.
- Filter admin cleanup by `TestRunId IS NOT NULL`.
- Optionally run scheduled cleanup for stale test runs.

This avoids per-feature custom test plumbing and scales across app schemas.

---

## Notes and limitations

- `testRunId` is a context tag, not a security boundary.
- It does not by itself prevent writes to production tables; backend policy must enforce isolation behavior.
- Existing clients continue to work without sending `TestRunId`.

---

## Files involved

Frontend:

- `src/automaton-studio-vue/src/features/apps/stores/app-editor.store.ts`
- `src/automaton-studio-vue/src/features/apps/services/apps.service.ts`
- `src/automaton-studio-vue/src/features/apps/utils/preview-api-script.ts`
- `src/automaton-studio-vue/src/features/apps/scripts/automaton-api-preview.js`

Backend:

- `src/Automaton.Studio.Server/Constants/AppExecutionContextKeys.cs`
- `src/Automaton.Studio.Server/Controllers/AppsFlowExecutionController.cs`
- `src/Automaton.Studio.Server/Controllers/AppsControllerExecutionController.cs`
- `src/Automaton.Studio.Server/Controllers/AppsStepExecutionController.cs`
- `src/Automaton.Studio.Server/Models/AppControllerModels.cs`
- `src/Automaton.Studio.Server/Models/AppStepExecution.cs`
