# AI Provider Registry Resolution

This document explains how AI provider lookup works in Automaton Studio when both hardcoded providers and admin-configured providers exist.

## Short Answer

Provider resolution is two-layered:

1. Hardcoded provider registry first.
2. Database-backed dynamic loading second (with caching).

So admin providers do not always need a custom C# provider class. If they are OpenAI-compatible, they can be created in Admin and loaded dynamically.

## Components Involved

- Hardcoded registry bootstrap:
  - `src/Automaton.Mcp/Extensions/ServiceCollectionExtensions.cs`
- Dynamic decorator registry:
  - `src/Automaton.Studio.Server/Services/DynamicAiProviderRegistry.cs`
- DI replacement in server startup:
  - `src/Automaton.Studio.Server/Program.cs`

## Lookup Order

When code requests a provider name, `DynamicAiProviderRegistry.GetProvider(name)` does:

1. Check hardcoded registry (`_hardcoded.GetProvider(name)`).
2. If not found, check dynamic cache (`_dynamic.TryGetValue(name, out ...)`).
3. If still not found, query database and build provider (`LoadFromDatabase(name)`).
4. Cache dynamic provider by name for next calls.

## What Admin Providers Need To Work

A provider created in Admin can be used dynamically when all are true:

1. Provider is active.
2. `BaseUrl` is set.
3. At least one active model exists.

If these conditions are met, `LoadFromDatabase` builds:

- `DeepInfraProvider` when provider name equals `DeepInfra`.
- Otherwise `OpenAiCompatibleProvider`.

## Collision Rule: Hardcoded Wins

If an admin provider uses a name that already exists in hardcoded registry (for example `OpenAI`), the hardcoded provider is returned first.

Implication:

- Name collisions do not override built-ins.
- Use distinct names for custom providers.

## Why Provider Name Is Sensitive

Provider name is used as a key in multiple places:

- Registry lookup.
- API key provider mapping.
- User settings/default model provider references.
- Metadata/model queries.

Because of this, renaming a provider safely requires cascade updates across all name-based references.

## Practical Guidance

Use hardcoded providers for:

- Native implementations with custom protocol handling.

Use admin-created dynamic providers for:

- OpenAI-compatible endpoints where base URL + models are enough.

If adding a provider that needs special behavior, implement a dedicated provider class and branch in `LoadFromDatabase` by provider name (as done for `DeepInfra`).
