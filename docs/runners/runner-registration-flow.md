# Runner Registration Flow

## Overview

This document describes how the Automaton Runner Console handles registration, re-registration, and cleanup of runner instances.

Note: Runner deployment is preferred via Docker Compose using `docker-compose.runner.yml`. Manual console execution is deprecated for production; use containerized runner and manage it with Compose. Console commands shown here are for understanding the runner internals and for rare debugging scenarios (e.g., `docker compose exec runner automaton-runner ...`).

## Key Concepts

### Runner Identity
Each runner has a unique GUID identifier that is:
- Generated on first registration
- Stored locally in `~/.automaton-runner/runner.json` (or equivalent on your OS)
- Persisted in the database with the runner's name and connection information

### Registration States

1. **New Registration**: No runner ID exists locally
   - A new GUID is generated
   - Runner is registered with the server via POST `/api/runners`
   - ID and name are stored locally

2. **Re-registration**: Runner ID exists locally but name is being changed
   - Existing GUID is reused
   - Runner name is updated on the server via PUT `/api/runners/{id}`
   - Updated name is stored locally

3. **Cleanup**: Local configuration is cleared
   - Runner configuration is deleted from local storage
   - Database entry remains intact
   - Re-registration will reuse the same runner ID if performed immediately

## Registration Flow

```
┌─────────────────────────────────────────────────────────────┐
│                    automaton-runner register                │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
                 ┌────────────────────────┐
                 │ Check for existing ID  │
                 │ in local storage       │
                 └────────────────────────┘
                              │
              ┌───────────────┴───────────────┐
              │                               │
         ID EXISTS                        ID MISSING
              │                               │
              ▼                               ▼
    ┌──────────────────┐           ┌──────────────────┐
    │ UPDATE operation │           │ CREATE operation │
    │ PUT /runners/:id │           │ POST /runners    │
    └──────────────────┘           └──────────────────┘
              │                               │
              └───────────────┬───────────────┘
                              │
                              ▼
                   ┌─────────────────────┐
                   │ Save to local       │
                   │ storage:            │
                   │ - Runner ID         │
                   │ - Runner Name       │
                   │ - Timestamp         │
                   └─────────────────────┘
```

## Benefits of This Approach

### 1. No Orphaned Database Entries
- Re-registration with a new name updates the existing entry
- Prevents accumulation of stale runner records
- Maintains database cleanliness

### 2. Preserves Runner History
- Connection logs and execution history remain associated with the same runner ID
- Easier to track runner activity over time

### 3. Flexible Name Changes
- Users can rename runners without creating duplicates
- Useful for reorganizing runner naming conventions

## Common Scenarios

### Scenario 1: Renaming a Runner

```bash
# Initial registration
# Preferred: manage runner via Docker Compose; for debugging inside container:
$ docker compose exec runner automaton-runner register
Enter runner name: My-Runner
✓ Runner 'My-Runner' registered successfully!

# Later, rename it
$ docker compose exec runner automaton-runner register
? Runner already registered: My-Runner (ID: abc-123)
Do you want to register with a different name? (y/N): y
Enter runner name: Production-Runner
✓ Runner updated successfully!
```

### Scenario 2: Cleanup and Re-register

```bash
# Clear local settings
$ docker compose exec runner automaton-runner cleanup
✓ Cleanup completed successfully!

# Re-register (will reuse the same ID if available)
$ docker compose exec runner automaton-runner register
Enter runner name: New-Runner-Name
✓ Runner updated successfully!
```

### Scenario 3: Manual Database Cleanup

If you have orphaned runners in the database (from before this feature), you can clean them up via the UI:

1. Go to Automaton Studio → Runners
2. Find the offline/orphaned runner
3. Click the "Delete" button
4. Confirm deletion

## Technical Implementation

### Client Side (Automaton.Runner.Console)

The `RunnerHostService.RegisterRunnerAsync` method:
1. Checks for existing runner ID in local storage
2. If found, performs PUT request to update
3. If not found, performs POST request to create
4. Saves the runner ID and name locally

### Server Side (Automaton.Api)

- **POST /api/runners**: Creates a new runner (validates name uniqueness)
- **PUT /api/runners/{id}**: Updates existing runner name
- **DELETE /api/runners/{id}**: Deletes a runner

The `RunnerDetailsValidator` allows updates when:
- The runner ID exists in the database
- The new name doesn't conflict with another runner (or it's the same name)

## Migration Notes

For existing deployments:
- Old runners continue to work without changes
- First re-registration will create a new entry (old behavior)
- Subsequent re-registrations will update the entry (new behavior)
- Use the delete button in the UI to clean up any orphaned entries

## Best Practices

1. **Use descriptive names**: Choose clear, environment-specific names (e.g., `Production-Server-1`, `Dev-Local`)
2. **Regular cleanup**: Periodically review the Runners page and delete inactive runners
3. **Document runner IDs**: Keep track of which runner IDs are deployed where, especially in production
4. **Avoid frequent name changes**: While supported, frequent renaming can make logs harder to track

## Troubleshooting

### Problem: "Runner already exists" error
**Solution**: The name is already taken by another runner. Choose a different name or delete the conflicting runner via the UI.

### Problem: Multiple entries for the same physical runner
**Solution**: This can happen if the local storage was cleared multiple times. Delete the duplicates via the UI, keeping only the active one.

### Problem: Cannot update runner name
**Solution**: Ensure you're authenticated with an API key that has sufficient permissions (not ReadOnly).
