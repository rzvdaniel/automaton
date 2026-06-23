<script setup lang="ts">
type AppEditorTab = 'build' | 'code' | 'backend-server' | 'ai' | 'logs' | 'settings'
type NavTarget = AppEditorTab | 'versions'

interface NavItem {
  key: string
  label: string
  icon: string
  disabled?: boolean
}

interface NavGroup {
  label?: string
  items: ReadonlyArray<NavItem>
}

defineProps<{
  activeItem: NavTarget
  groups: ReadonlyArray<NavGroup>
}>()

const emit = defineEmits<{
  select: [value: string]
}>()
</script>

<template>
  <aside class="app-editor-side-nav" aria-label="App navigation">
    <div
      v-for="group in groups"
      :key="group.label ?? 'settings'"
      class="side-nav-group"
    >
      <div v-if="group.label" class="side-nav-group-label">{{ group.label }}</div>
      <button
        v-for="item in group.items"
        :key="item.key"
        type="button"
        class="side-nav-item"
        :class="{ active: activeItem === item.key }"
        :disabled="item.disabled"
        @click="emit('select', item.key)"
      >
        <i :class="item.icon"></i>
        <span>{{ item.label }}</span>
      </button>
    </div>
  </aside>
</template>

<style scoped>
.app-editor-side-nav {
  width: 14rem;
  flex-shrink: 0;
  padding: 1rem 0.75rem;
  border-right: 1px solid var(--p-surface-200);
  background: var(--p-surface-0);
  overflow-y: auto;
}

.side-nav-group + .side-nav-group {
  margin-top: 1rem;
}

.side-nav-group-label {
  padding: 0 0.75rem 0.35rem;
  color: var(--p-text-muted-color);
  font-size: 0.72rem;
  font-weight: 700;
  letter-spacing: 0.04em;
  text-transform: uppercase;
}

.side-nav-item {
  display: flex;
  align-items: center;
  gap: 0.625rem;
  width: 100%;
  padding: 0.625rem 0.75rem;
  border: none;
  border-radius: 0.75rem;
  background: transparent;
  color: var(--p-text-color);
  font-size: 0.875rem;
  text-align: left;
  cursor: pointer;
  transition: background-color 0.15s ease, color 0.15s ease;
}

.side-nav-item:hover:not(:disabled) {
  background: var(--p-surface-100);
}

.side-nav-item.active {
  background: var(--p-primary-100);
  color: var(--p-primary-700);
  font-weight: 600;
}

.side-nav-item:disabled {
  opacity: 0.48;
  cursor: not-allowed;
}

.side-nav-item i {
  width: 1rem;
  font-size: 0.9rem;
  text-align: center;
}

:global(.app-dark) .app-editor-side-nav {
  border-color: var(--p-surface-700);
  background: var(--p-surface-900);
}

:global(.app-dark) .side-nav-item:hover:not(:disabled) {
  background: var(--p-surface-800);
}

:global(.app-dark) .side-nav-item.active {
  background: var(--p-primary-900);
  color: var(--p-primary-200);
}

@media (max-width: 960px) {
  .app-editor-side-nav {
    width: 100%;
    padding: 0.75rem;
    border-right: none;
    border-bottom: 1px solid var(--p-surface-200);
  }

  .side-nav-group {
    display: flex;
    flex-direction: column;
  }

  .side-nav-group + .side-nav-group {
    margin-top: 0.75rem;
  }
}
</style>
