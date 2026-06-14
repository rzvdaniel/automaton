---
name: vue-component-splitting
description: Refactor Vue components with heavy conditional logic into separate, focused components.
---

# Vue Component Splitting Pattern

Use this skill when editing Vue components in Automaton, especially under:

- `src/automaton-studio-vue/`
- `src/automaton-vue/`

This skill applies when a Vue component has grown hard to understand because it mixes multiple distinct UI states, roles, or workflows in one file.

---

## When to apply this pattern

Use this pattern when you encounter:

- large `v-if` / `v-else` blocks with different UI structures
- different user roles or app states requiring distinct behavior
- page components exceeding roughly 300–400 lines
- conditional blocks exceeding roughly 50 lines each
- mixed logic for unrelated scenarios in one `<script setup>`
- tests that are hard to write because too many paths are coupled
- repeated merge conflicts in a large view component

Do not split just because a component has a small conditional. Split when the branches are conceptually different.

---

## Anti-pattern

```vue
<!-- DashboardView.vue - before -->
<template>
  <div>
    <PendingInvitationsCard />

    <template v-if="isAppUserDashboard">
      <!-- 100+ lines of AppUser-specific UI -->
      <Card>...</Card>
      <Button @click="handleAppUserAction">...</Button>
    </template>

    <template v-else>
      <!-- 300+ lines of standard dashboard UI -->
      <WelcomeSection />
      <div class="stats-grid">...</div>
      <Card>...</Card>
      <Button @click="handleStandardAction">...</Button>
    </template>
  </div>
</template>

<script setup lang="ts">
const isAppUserDashboard = computed(...)

const handleAppUserAction = () => {
  // AppUser logic
}

const handleStandardAction = () => {
  // Standard user logic
}

// Mixed concerns, hard to test.
</script>
```

Problems:

- the reader must understand all paths at once
- unrelated role-specific logic is mixed together
- tests need to mock multiple states for one large component
- small changes risk breaking unrelated branches
- the component becomes a frequent merge-conflict point

---

## Recommended pattern

Extract each large conditional path into a focused component. Keep the original view as a thin orchestrator.

### Step 1: Create focused components

```vue
<!-- components/StandardDashboard.vue -->
<script setup lang="ts">
import { onMounted } from 'vue'
import { useDashboardStore } from '../stores/dashboard.store'

const dashboardStore = useDashboardStore()

onMounted(async () => {
  await dashboardStore.fetchDashboard()
  dashboardStore.startAutoRefresh(30000)
})

const handleCreateApp = () => {
  // Standard dashboard logic.
}
</script>

<template>
  <div class="standard-dashboard">
    <div class="header">
      <h1>Dashboard</h1>
      <Button label="Create App" @click="handleCreateApp" />
    </div>

    <WelcomeSection />
    <div class="stats-grid">...</div>
  </div>
</template>

<style scoped>
.standard-dashboard {
  display: grid;
  gap: 1rem;
}
</style>
```

```vue
<!-- components/AppUserDashboard.vue -->
<script setup lang="ts">
import { useRouter } from 'vue-router'

const router = useRouter()

const handleViewMyApps = () => {
  router.push('/my-apps')
}
</script>

<template>
  <div class="app-user-dashboard">
    <div class="header">
      <h1>Dashboard</h1>
      <p>Access your assigned applications</p>
    </div>

    <Card>
      <template #content>
        <Button label="View My Apps" @click="handleViewMyApps" />
      </template>
    </Card>
  </div>
</template>

<style scoped>
.app-user-dashboard {
  display: grid;
  gap: 1rem;
}
</style>
```

### Step 2: Keep the parent as an orchestrator

```vue
<!-- views/DashboardView.vue -->
<script setup lang="ts">
import { computed, onMounted } from 'vue'
import { useOrganizationRole } from '@/features/organizations/composables/useOrganizationRole'
import PendingInvitationsCard from '@/features/organizations/components/PendingInvitationsCard.vue'
import AppUserDashboard from '../components/AppUserDashboard.vue'
import StandardDashboard from '../components/StandardDashboard.vue'

const { isAppUserOnly, loadOrganizations, organizations } = useOrganizationRole()

const hasOrganizations = computed(() => organizations.value.length > 0)
const isAppUserDashboard = computed(() => isAppUserOnly.value && hasOrganizations.value)

onMounted(async () => {
  await loadOrganizations()
})
</script>

<template>
  <div class="dashboard-view">
    <PendingInvitationsCard />

    <AppUserDashboard v-if="isAppUserDashboard" />
    <StandardDashboard v-else />
  </div>
</template>

<style scoped>
.dashboard-view {
  max-width: 1400px;
  margin: 0 auto;
}
</style>
```

---

## Decision criteria

### Split components when

- conditional branches exceed roughly 50 lines each
- the branches represent different roles, workflows, or states
- each branch has different event handlers or data-loading needs
- reading one scenario requires mentally filtering out another scenario
- the parent component is becoming an unreadable page object

### Keep the conditional inline when

- the variation is only a small show/hide section
- the structure is the same and only data changes
- extracting would create tiny one-use components with no clarity gain
- shared logic would need excessive prop drilling
- the whole component is still small and easy to read

---

## File organization

Prefer feature-local components.

```txt
features/dashboard/
├── views/
│   └── DashboardView.vue
├── components/
│   ├── StandardDashboard.vue
│   ├── AppUserDashboard.vue
│   ├── WelcomeSection.vue
│   └── StatsCard.vue
└── stores/
    └── dashboard.store.ts
```

Avoid creating broad global component folders for feature-specific UI.

---

## Common pitfalls

### Over-splitting

Do not create a new component for every small condition. The goal is clarity, not more files.

### Prop drilling

If the extracted component needs five or more props, reconsider the split. Options:

- move data loading into the child
- extract a composable
- use a feature store
- use provide/inject only when it genuinely simplifies the design

### Duplicated logic

Do not copy-paste shared logic into both branches. Extract shared behavior to:

- a composable
- a store action
- a shared helper
- a smaller shared component

### Inconsistent role handling

If one role-based page uses the orchestrator pattern, similar role-based pages should follow the same pattern unless there is a good reason not to.

---

## Automaton example

Automaton’s dashboard used this pattern:

```txt
Before:
- DashboardView.vue: around 544 lines with complex conditionals

After:
- DashboardView.vue: thin orchestrator
- StandardDashboard.vue: standard user dashboard
- AppUserDashboard.vue: simplified assigned-app user dashboard
```

Result:

- easier onboarding
- clearer testing
- simpler maintenance
- less chance of breaking one role while editing another

---

## Verification

After applying this pattern in Studio frontend:

```bash
cd src/automaton-studio-vue
npm run type-check
npm run build
```

After applying it in the website:

```bash
cd src/automaton-vue
npm run build
```

Also check:

- desktop layout still works
- mobile layout still works
- loading state still works
- empty state still works
- error state still works
- role-specific behavior still routes to the correct component

For the full verification matrix, read:

```txt
.agent/harness/verification.md
```

---

## Related skills

Consider reading:

```txt
.agent/skills/studio-frontend/SKILL.md
```

when it exists.

For repository-wide rules, read:

```txt
AGENTS.md
```
