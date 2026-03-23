---
applyTo: "**/*.vue,**/src/**/*.ts,**/src/**/*.tsx"
---

# Vue 3 / TypeScript Coding Standards — Automaton

## Architecture: Feature-Based Structure

Every feature lives in `src/features/<name>/` with this layout:

```
src/features/flows/
├─ components/    # Vue components
├─ composables/   # Composition API hooks (useFlows.ts)
├─ services/      # API calls only (flowsService.ts)
├─ stores/        # Pinia state (flowsStore.ts)
├─ types/         # TypeScript interfaces
├─ views/         # Page-level components
└─ routes.ts      # Feature routes
```

Keep stores lean. Colocate all feature logic within the feature folder.

## TypeScript

- Use `const` objects instead of TypeScript enums — enums create a separate type namespace and don't interop cleanly with C# JSON strings:
  ```ts
  // ✅ DO
  export const ChatResourceType = { General: 'General', WebApp: 'WebApp' } as const
  type ChatResourceType = typeof ChatResourceType[keyof typeof ChatResourceType]

  // ❌ DON'T
  enum ChatResourceType { General, WebApp }
  ```
- Match C# DTO property names exactly — casing, optionality, and types must align
- Run `npm run generate:types` to regenerate `src/shared/types/api/` from the OpenAPI spec after backend changes

## Components

- Use `<script setup lang="ts">` (Composition API) — no Options API
- Extract props types with `defineProps<{ ... }>()`
- **Split heavy conditionals**: when a component has large `v-if/v-else` blocks (>50 lines each) with diverging logic, extract each path into a separate component with a thin orchestrator view
- Never hardcode `background-color` in nested components — inherit from parent via CSS variables

## State Management (Pinia)

- One store per feature: `useFlowsStore()`, `useAppsStore()`, etc.
- Stores hold reactive state only — business logic goes in composables or services
- Access via `useXxxStore()` inside `<script setup>`

## HTTP / API

- All API calls go through the Axios instance at `src/shared/services/api/axios-instance.ts` — it injects JWT automatically
- Services are plain functions that return typed promises — no business logic
- Always handle errors with try/catch and show toast notifications via `useToast()`

## Toast / Notifications (Critical)

- **Never add `<Toast />` to any component other than `App.vue`** — `<Toast />` is registered globally in `App.vue` and must appear exactly once in the entire app. Adding it in any view or child component causes every `toast.add()` call to render duplicate notifications.
- To show a toast anywhere in the app, simply call `useToast()` — the global instance handles rendering:
  ```ts
  // ✅ DO — just use the composable, no <Toast /> needed
  const toast = useToast()
  toast.add({ severity: 'success', summary: 'Saved', life: 3000 })

  // ❌ DON'T — never add <Toast /> in views or components
  // <Toast />  ← removes this from templates; App.vue already has it
  ```

## Theme-Aware CSS (Critical)

- **Never set `background-color` in nested components** — this breaks dark mode
- Use CSS variables from PrimeVue's design token system:
  - `var(--p-surface-ground)` — page background
  - `var(--p-surface-card)` — card/panel background  
  - `var(--p-text-color)` — body text
  - `var(--p-surface-400)` — muted/secondary text
- Dark mode is toggled via `.app-dark` class on the root; nest dark overrides with `.app-dark &`

## Every Async Operation Needs

1. A loading state (`isLoading.value = true/false`)
2. A try/catch with a toast on error
3. Cleanup in `finally` to reset loading state

## Chat / AI Input Areas

Use the bordered-container pattern (same as GitHub Copilot, ChatGPT, Claude) for all AI prompt inputs:

- Textarea + action buttons live **inside** a single bordered rounded container — never side-by-side with floating buttons
- Send button sits **bottom-right inside the box**; Cancel (stop-circle) replaces it during generation
- **Enter = send**, **Shift+Enter = newline** — always use `@keydown` not `@keypress`
- No "Clear chat" button in the input row — users navigate back to sessions to start fresh
- Use `text + rounded` button style for send/cancel for visual consistency

```vue
<!-- ✅ DO — bordered container pattern -->
<div class="prompt-box">
  <Textarea v-model="input" @keydown="(e) => { if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); send() } }" />
  <div class="prompt-actions">
    <Button v-if="isBusy" icon="pi pi-stop-circle" severity="danger" text rounded @click="cancel" />
    <Button v-else icon="pi pi-send" text rounded :disabled="!input.trim()" @click="send" />
  </div>
</div>

<!-- ❌ DON'T — floating buttons beside the textarea -->
<div class="flex gap-2 align-items-end">
  <Textarea v-model="input" class="flex-1" />
  <Button icon="pi pi-send" @click="send" />
  <Button icon="pi pi-trash" severity="secondary" text @click="clear" />
</div>
```

## Naming

- Components: `PascalCase.vue`
- Composables: `useCamelCase.ts`
- Services: `camelCaseService.ts`
- Stores: `useCamelCaseStore.ts`
- Types/interfaces: `PascalCase`, exported from `types/` folder
- Constants objects: `PascalCase` with `as const`

## Post-Change Validation — REQUIRED before marking a change complete

**A Vue/TypeScript change is NOT done until `npm run type-check` passes with 0 errors.**

```bash
cd src/automaton-studio-vue
npm run type-check   # vue-tsc — catches template + TS type mismatches
npm run lint         # ESLint
```

Rules:
- Run type-check after **every** edit to a `.vue`, `.ts`, or `.tsx` file
- Fix every error — do not suppress with `@ts-ignore` or `// @ts-nocheck`
- Errors in unrelated files you did not touch must also be fixed before completing
- Only mark the task done after a clean type-check run
