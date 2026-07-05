# AI Guidance Architecture

This document explains how Automaton uses AI guidance documents today:

- where guidance lives
- how Studio and CLI consume it
- how the database is seeded
- which document properties matter in Admin
- how to think about `BasePrompt` and `Guidance`

## Current model

Automaton now uses this rule:

- embedded files = packaged seed templates only
- database = source of truth for runtime and export

That means:

- Studio AI runtime reads guidance from the database
- MCP guidance tools read guidance from the database
- CLI guidance scaffold/export reads guidance from the database
- Admin UI edits guidance in the database
- embedded markdown files are only used to seed an empty database

## Main components

### Embedded packaged defaults

Code:

- [EmbeddedAutomatonGuidanceProvider.cs](/C:/Work/automaton/src/Automaton.AiGuidance/EmbeddedAutomatonGuidanceProvider.cs)
- [AutomatonGuidanceSeedCatalog.cs](/C:/Work/automaton/src/Automaton.AiGuidance/AutomatonGuidanceSeedCatalog.cs)

Purpose:

- read packaged markdown files included with the application
- provide the initial seed set when the database is empty
- provide packaged templates used by CLI-generated local files such as `AGENTS.md`

Important:

- embedded guidance is no longer used as runtime prompt truth
- embedded guidance is no longer used as CLI guidance truth

### Runtime guidance service

Code:

- [AiGuidanceRuntimeService.cs](/C:/Work/automaton/src/Automaton.Api/Services/AiGuidanceRuntimeService.cs)

Purpose:

- resolve base prompts from DB
- resolve always-loaded guidance from DB
- list loadable guidance from DB
- fetch guidance by key from DB
- build the CLI export pack from DB
- seed missing defaults into DB from embedded packaged files

This is the runtime source of truth.

### Admin guidance management

Code:

- [AdminAiGuidanceController.cs](/C:/Work/automaton/src/Automaton.Api/Controllers/Admin/AdminAiGuidanceController.cs)
- [AdminAiGuidanceService.cs](/C:/Work/automaton/src/Automaton.Api/Services/AdminAiGuidanceService.cs)
- [AdminAiGuidanceView.vue](/C:/Work/automaton/src/automaton-studio-vue/src/features/admin/views/AdminAiGuidanceView.vue)

Purpose:

- create, edit, enable, disable, and delete guidance documents
- manage the metadata used by runtime resolution

There is no longer a reset-to-embedded-default action.

### CLI export endpoint

Code:

- [AiGuidanceController.cs](/C:/Work/automaton/src/Automaton.Api/Controllers/AiGuidanceController.cs)

Endpoint:

- `GET /api/ai-guidance/export`

Purpose:

- export the current enabled guidance pack from the database for CLI scaffold/sync

The exported file layout is synthesized by the API:

- `llms.txt`
- `llms-full.txt`
- `llms/{key}.md`

The database does not store file paths. Export layout is an API concern.

## How the database is seeded

Code:

- [DataExtensions.cs](/C:/Work/automaton/src/Automaton.Api/Extensions/DataExtensions.cs)
- [AiGuidanceRuntimeService.cs](/C:/Work/automaton/src/Automaton.Api/Services/AiGuidanceRuntimeService.cs)

At startup, the API calls:

- `SeedMissingDefaultsAsync()`

Seed behavior:

1. Read packaged embedded guidance files from `EmbeddedAutomatonGuidanceProvider`.
2. Resolve metadata from `AutomatonGuidanceSeedCatalog`.
3. Insert only missing keys into `AiGuidanceDocuments`.
4. Do not overwrite existing DB rows.

This means:

- first boot can populate the DB
- local development can bootstrap a fresh DB
- existing edited guidance is preserved

## How Studio uses guidance

### Studio AI system prompt assembly

Code:

- [AppSystemPromptBuilder.cs](/C:/Work/automaton/src/Automaton.Mcp/SystemPrompts/AppSystemPromptBuilder.cs)

The Studio app editor prompt builder does this:

1. Load the shared base prompt:
   - `GetBasePromptAsync("shared")`
2. Load required Studio guidance:
   - `GetRequiredGuidanceAsync("studio")`
3. Select extra optional guidance keys based on the user request:
   - for example frontend structure, backend controllers, deploy guidance
4. Resolve those optional documents by key:
   - `GetGuidanceByKeyAsync(key)`
5. Build the final system prompt seen by the model

Important distinction:

- required Studio guidance is always injected
- optional guidance is selected and loaded as needed

### MCP guidance tools

Code:

- [AutomatonGuidanceTool.cs](/C:/Work/automaton/src/Automaton.Mcp/Tools/AutomatonGuidanceTool.cs)

The model can:

- list loadable guidance:
  - `ListLoadableGuidanceAsync()`
- fetch one by key:
  - `GetGuidanceByKeyAsync(key)`

Only model-loadable guidance is exposed through these tools.

Base prompts are not discoverable through the tool.

## How CLI uses guidance

Code:

- [AutomatonApiClient.cs](/C:/Work/automaton/src/Automaton.Cli/Services/AutomatonApiClient.cs)
- [AiGuidancePackService.cs](/C:/Work/automaton/src/Automaton.Cli/Services/AiGuidancePackService.cs)
- [NewAppService.cs](/C:/Work/automaton/src/Automaton.Cli/Services/NewAppService.cs)
- [LinkAppService.cs](/C:/Work/automaton/src/Automaton.Cli/Services/LinkAppService.cs)
- [PullCommand.cs](/C:/Work/automaton/src/Automaton.Cli/Commands/PullCommand.cs)
- [SyncAiCommand.cs](/C:/Work/automaton/src/Automaton.Cli/Commands/SyncAiCommand.cs)

Current flow:

1. CLI calls `GET /api/ai-guidance/export`
2. API returns the current enabled guidance pack from DB
3. CLI writes it into:
   - `.automaton/agents/references/llms.txt`
   - `.automaton/agents/references/llms-full.txt`
   - `.automaton/agents/references/llms/*.md`
4. CLI writes local wrapper/template files like:
   - `AGENTS.md`
   - `.automaton/agents/instructions.md`
   - `.automaton/agents/skills/automaton-app/SKILL.md`

So:

- guidance content comes from DB
- local wrapper/template files still come from packaged templates

### CLI commands that use guidance

- `automaton new`
- `automaton link`
- `automaton pull`
- `automaton sync-ai`

Behavior:

- `new`, `link`, and `pull` try to fetch latest guidance from the API
- if guidance export is unavailable, they continue and show a warning
- `sync-ai` is strict and fails if server-backed guidance cannot be fetched

## Document kinds

The key runtime distinction is `DocumentKind`.

### BasePrompt

Meaning:

- harness foundation
- always injected by the runtime
- not model-loadable

Typical characteristics:

- small
- stable
- high-level contract

Example:

- `automaton-app-contract-compact`

Typical metadata:

- `DocumentKind = BasePrompt`
- `RequiredByDefault = true`
- `IsModelLoadable = false`
- low `SortOrder` such as `0`

### Guidance

Meaning:

- focused instruction document
- can be always loaded or optional
- can be discoverable by the model

Typical uses:

- Studio editor rules
- backend controller rules
- CLI deploy help
- MCP-specific guidance
- examples and reference patterns

Typical metadata:

- `DocumentKind = Guidance`
- `RequiredByDefault = true` or `false`
- `IsModelLoadable = true` in most cases

## Document properties in Admin

In Studio Admin -> Guides, these fields matter.

### Key

Stable identifier used by runtime and tools.

Examples:

- `automaton-studio-editor`
- `automaton-cli-deploy`

Rules:

- should be stable
- should not be treated as a human title
- export path is derived from key:
  - `llms/{key}.md`

### Title

Human-readable name shown in the UI.

Examples:

- `Automaton Studio Editor`
- `Automaton CLI Deploy`

### Surface

Primary target surface for the document.

Current surfaces:

- `shared`
- `studio`
- `cli`
- `mcp`

Meaning:

- `shared` = cross-surface prompt material
- `studio` = Studio editor/runtime-specific guidance
- `cli` = CLI-specific guidance
- `mcp` = MCP-specific guidance

### DocumentKind

Role of the document:

- `BasePrompt`
- `Guidance`

This is one of the most important fields.

### RequiredByDefault

Whether the runtime should inject the document automatically for its surface.

Meaning:

- `true` = always loaded for that runtime surface
- `false` = optional

Examples:

- `automaton-studio-editor` can be `RequiredByDefault = true`
- `automaton-cli-deploy` is usually optional and not injected into Studio prompts

### IsModelLoadable

Whether the model can discover or fetch the document through guidance tooling.

Meaning:

- `true` = appears in loadable guidance listings and can be fetched by key
- `false` = hidden from model discovery

Typical rule:

- base prompts should usually be `false`
- optional and discoverable guidance should usually be `true`

### SortOrder

Ordering hint inside runtime selection and UI grouping.

Use it sparingly.

Typical pattern:

- base prompt: `0`
- required surface guidance: low value like `10` or `100`
- optional references: higher values like `200`

### Description

Short explanation of what the document is for.

Used for:

- Admin clarity
- guidance tool listing
- export metadata intent

### Tags

Lightweight discovery metadata.

Used for:

- search/filtering
- model discovery hints

## What admins should configure for Studio and CLI

### Shared base prompt

Recommended shape:

- `Surface = shared`
- `DocumentKind = BasePrompt`
- `RequiredByDefault = true`
- `IsModelLoadable = false`

This is the main harness contract.

### Studio always-loaded guidance

Recommended shape:

- `Surface = studio`
- `DocumentKind = Guidance`
- `RequiredByDefault = true`
- `IsModelLoadable = true`

This is for documents like:

- Studio editor behavior
- file editing and verification conventions

### Studio optional guidance

Recommended shape:

- `Surface = studio` or `shared`
- `DocumentKind = Guidance`
- `RequiredByDefault = false`
- `IsModelLoadable = true`

Use this for:

- topic-specific rules
- examples
- optional reference material

### CLI guidance

Recommended shape:

- `Surface = cli`
- `DocumentKind = Guidance`
- `RequiredByDefault = false`
- `IsModelLoadable = true`

These documents are exported into local CLI-generated guidance packs, not injected into Studio runtime prompts by default.

### MCP guidance

Recommended shape:

- `Surface = mcp`
- `DocumentKind = Guidance`
- `RequiredByDefault = false`
- `IsModelLoadable = true`

Use this for MCP-specific workflows and constraints.

## How the AI model sees documents

### In Studio

The model sees:

- the shared base prompt
- required Studio guidance
- any optional guidance selected for the current request

Optional guidance is loaded by key after relevance selection.

### In MCP guidance tool flows

The model can:

- list loadable documents
- request one by key

It does not automatically see every guidance document.

### In CLI-generated local guidance

The local agent or assistant sees:

- generated wrapper files such as `AGENTS.md`
- exported `llms.txt`
- exported `llms/*.md`

Those files reflect the current DB state at the time the CLI sync/export happened.

## Can we add more guidance documents?

Yes.

That is the expected model.

You can add:

- more optional guidance
- more required guidance for a surface
- additional base prompts later, if the runtime design explicitly supports them

When adding a new document, decide:

1. Which surface is it for?
2. Is it a `BasePrompt` or `Guidance` document?
3. Should it always be injected?
4. Should the model be allowed to discover/load it?

## Recommended current design patterns

### Single true base prompt

Good current pattern:

- one shared `BasePrompt`
- detailed surface behavior stays in `Guidance`

This keeps the harness stable and avoids too many hardwired prompt layers.

### Required guidance for surface behavior

Good current pattern:

- Studio-specific editing rules stay as `Guidance`
- mark them `RequiredByDefault = true` if they must always be injected

This avoids treating every important document as a base prompt.

### Optional discoverable references

Good current pattern:

- examples
- backend rules
- CLI deploy help
- MCP help

These should usually be:

- `DocumentKind = Guidance`
- `RequiredByDefault = false`
- `IsModelLoadable = true`

## Summary

Today the guidance architecture is:

- packaged markdown seeds an empty DB
- Admin edits the DB
- Studio runtime reads the DB
- MCP guidance tools read the DB
- CLI exports the DB into local guidance files

The key distinction between document categories is:

- `BasePrompt` = always-injected harness foundation, normally not discoverable
- `Guidance` = focused instruction docs, either always-loaded or optional/discoverable

If in doubt:

- use `Guidance` by default
- use `BasePrompt` only for true harness-level foundation
- keep `IsModelLoadable = false` for base prompts
- keep the DB as the only runtime truth
