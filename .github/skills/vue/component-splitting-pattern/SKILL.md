---
title: Vue Component Splitting Pattern
description: Refactor components with heavy conditional logic into separate, focused components
category: vue
tags: [vue, architecture, refactoring, components, best-practices]
complexity: intermediate
---

# Vue Component Splitting Pattern

## When to Apply This Pattern

Use this pattern when you encounter:
- **Heavy conditional rendering** - Large `v-if/v-else` blocks with different UI structures
- **Diverging logic** - Different user roles/states requiring distinct behaviors
- **Template bloat** - Single component file exceeding 300-400 lines
- **Testing complexity** - Difficulty isolating test scenarios due to conditional paths
- **Maintenance burden** - Developers struggling to understand all code paths

## Anti-Pattern Example

```vue
<!-- DashboardView.vue - BEFORE (Anti-pattern) -->
<template>
  <div>
    <PendingInvitationsCard />
    
    <!-- Heavy conditional logic -->
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

<script setup>
// Jumbled logic for both scenarios
const isAppUserDashboard = computed(...)
const handleAppUserAction = () => { ... }
const handleStandardAction = () => { ... }
// Mixed concerns, hard to test
</script>
```

**Problems:**
- Cognitive overload - must understand all paths simultaneously
- Test complexity - need to mock multiple user states
- Merge conflicts - multiple developers editing same large file
- Reusability - can't reuse AppUser dashboard elsewhere

## Recommended Pattern

### Step 1: Create Separate Component Files

Extract each conditional path into its own component:

```vue
<!-- components/StandardDashboard.vue -->
<script setup lang="ts">
import { onMounted } from 'vue'
import { useDashboardStore } from '../stores/dashboard.store'
// Only imports needed for standard dashboard

const dashboardStore = useDashboardStore()

onMounted(async () => {
  await dashboardStore.fetchDashboard()
  dashboardStore.startAutoRefresh(30000)
})

const handleCreateApp = () => {
  // Standard dashboard logic
}
</script>

<template>
  <div class="standard-dashboard">
    <div class="header">
      <h1>Dashboard</h1>
      <Button label="Create App" @click="handleCreateApp" />
    </div>
    
    <!-- All standard dashboard UI here -->
    <WelcomeSection />
    <div class="stats-grid">...</div>
  </div>
</template>

<style scoped>
/* Scoped styles for standard dashboard only */
</style>
```

```vue
<!-- components/AppUserDashboard.vue -->
<script setup lang="ts">
import { useRouter } from 'vue-router'
// Only imports needed for AppUser dashboard

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
    
    <!-- Simplified AppUser UI -->
    <Card>
      <template #content>
        <Button label="View My Apps" @click="handleViewMyApps" />
      </template>
    </Card>
  </div>
</template>

<style scoped>
/* Scoped styles for AppUser dashboard only */
</style>
```

### Step 2: Create Orchestrator Component

The parent view becomes a thin orchestrator:

```vue
<!-- views/DashboardView.vue -->
<script setup lang="ts">
import { onMounted, computed } from 'vue'
import { useOrganizationRole } from '@/features/organizations/composables/useOrganizationRole'
import StandardDashboard from '../components/StandardDashboard.vue'
import AppUserDashboard from '../components/AppUserDashboard.vue'
import PendingInvitationsCard from '@/features/organizations/components/PendingInvitationsCard.vue'

const { isAppUserOnly, loadOrganizations, organizations } = useOrganizationRole()

const hasOrganizations = computed(() => organizations.value.length > 0)
const isAppUserDashboard = computed(() => isAppUserOnly.value && hasOrganizations.value)

onMounted(async () => {
  await loadOrganizations()
})
</script>

<template>
  <div class="dashboard-view">
    <!-- Shared components for all users -->
    <PendingInvitationsCard />
    
    <!-- Conditional component rendering -->
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

## Benefits of This Pattern

1. **Single Responsibility** - Each component has one clear purpose
2. **Testability** - Test each dashboard in isolation
3. **Readability** - Open `AppUserDashboard.vue` and see only relevant code
4. **Reusability** - Can reuse `AppUserDashboard` in other contexts
5. **Performance** - Smaller component trees, easier for Vue to optimize
6. **Collaboration** - Teams can work on different dashboards without conflicts

## File Organization

```
features/dashboard/
├── views/
│   └── DashboardView.vue          # Orchestrator (thin)
├── components/
│   ├── StandardDashboard.vue      # Full dashboard (self-contained)
│   ├── AppUserDashboard.vue       # Simplified dashboard (self-contained)
│   ├── WelcomeSection.vue         # Shared component
│   └── StatsCard.vue              # Shared component
└── stores/
    └── dashboard.store.ts
```

## Decision Criteria

**Split components when:**
- ✅ Conditional blocks exceed ~50 lines each
- ✅ Logic diverges significantly between paths
- ✅ Different roles/states have distinct UX
- ✅ You find yourself scrolling to understand one scenario

**Keep conditional when:**
- ❌ Minor UI variations (show/hide single element)
- ❌ Same structure, different data
- ❌ Shared complex logic between paths
- ❌ Total component < 150 lines

## Real-World Example

From Automaton's dashboard implementation:

- **Before**: DashboardView.vue with 544 lines, complex conditionals
- **After**: 
  - DashboardView.vue (orchestrator): 37 lines
  - StandardDashboard.vue: ~450 lines (focused)
  - AppUserDashboard.vue: ~57 lines (focused)

**Result**: Easier onboarding, clearer testing, simpler maintenance.

## Common Pitfalls

❌ **Over-splitting** - Don't create separate components for minor variations
❌ **Prop drilling** - If passing 5+ props, reconsider the split or use provide/inject
❌ **Duplicated logic** - Extract shared logic to composables, not copy-paste
❌ **Inconsistent patterns** - Use same pattern across similar features (all role-based views)

## Related Patterns

- **Feature-based architecture** - Group components by feature, not type
- **Composables for shared logic** - Use `useXxx()` for reusable state/behavior
- **Slots for customization** - When components share structure but vary content

## References

- [Vue.js Style Guide - Component Files](https://vuejs.org/style-guide/)
- [Automaton: docs/skills/implementing-features/SKILL.md](../../implementing-features/SKILL.md)
- [Automaton: AGENTS.md](../../../../AGENTS.md) - Component design principles
