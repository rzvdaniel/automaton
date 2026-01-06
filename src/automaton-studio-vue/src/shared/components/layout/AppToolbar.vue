<script setup lang="ts">
import { useTheme } from '@/shared/composables/useTheme'
import Toolbar from 'primevue/toolbar'
import Button from 'primevue/button'

const { isDark, toggleDarkMode } = useTheme()
</script>

<template>
  <Toolbar class="app-toolbar border-noround">
    <template #start>
      <div class="flex items-center gap-2">
        <i class="pi pi-bolt text-xl" style="color: var(--p-primary-500)"></i>
        <span class="font-bold text-xl">Automaton Studio</span>
      </div>
    </template>

    <template #center>
      <span class="text-color-secondary">Dashboard</span>
    </template>

    <template #end>
      <div class="flex items-center gap-2">
        <Button
          :icon="isDark ? 'pi pi-sun' : 'pi pi-moon'"
          severity="secondary"
          text
          rounded
          aria-label="Toggle Dark Mode"
          @click="toggleDarkMode"
        />
        <Button icon="pi pi-cog" severity="secondary" text rounded aria-label="Settings" />
        <Button icon="pi pi-user" severity="secondary" text rounded aria-label="User Menu" />
      </div>
    </template>
  </Toolbar>
</template>

<style scoped>
.app-toolbar {
  height: 64px;
  padding: 0 1rem;
  border-bottom: 1px solid var(--p-surface-200);
}

:deep(.app-dark) .app-toolbar {
  border-bottom-color: var(--p-surface-700);
}
</style>
