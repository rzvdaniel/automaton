# Automaton Repo Map

## Studio frontend

Root:
- src/automaton-studio-vue

App editor:
- src/features/apps/views/AppEditorView.vue
- src/features/apps/components/AppEditorSideNav.vue
- src/features/apps/components/AppEditorWorkspaceBar.vue
- src/features/apps/components/AppVersionHistory.vue
- src/features/apps/services/apps.service.ts
- src/features/apps/types/apps.types.ts

Agent harness:
- .agent/harness/*.ps1

Rules:
- App Editor navigation changes usually start in AppEditorView.vue and AppEditorSideNav.vue.
- Version snapshot UI usually starts in AppVersionHistory.vue.
- Files explorer filtering usually starts in apps.service.ts.