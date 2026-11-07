<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import { useUserSettings } from '@/shared/composables/useUserSettings'
import Button from 'primevue/button'
import { pageTitleForRoute } from './page-title'

const { isDark, toggleDarkMode } = useUserSettings()
const route = useRoute()

const pageTitle = computed(() => pageTitleForRoute(route.name as string | undefined, route.meta.title))
</script>

<template>
  <header class="app-toolbar">
    <span class="toolbar-title">{{ pageTitle }}</span>
    <div class="toolbar-end">
      <span
        v-tooltip.bottom="'Developer Preview\nAPIs and features may change as the platform evolves.'"
        class="preview-tag"
      >
        Preview
      </span>
      <Button
        :icon="isDark ? 'pi pi-sun' : 'pi pi-moon'"
        severity="secondary"
        text
        rounded
        aria-label="Toggle Dark Mode"
        @click="toggleDarkMode"
      />
    </div>
  </header>
</template>

<style scoped>
.app-toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  padding: 0 1.5rem;
}

.toolbar-title {
  font: 700 0.95rem var(--display);
  color: var(--ink-soft);
}

.toolbar-end {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.preview-tag {
  padding: 0.15rem 0.6rem;
  border: 1px solid var(--line);
  border-radius: 999px;
  color: var(--signal-ink);
  font: 500 0.72rem var(--mono);
  letter-spacing: 0.04em;
  text-transform: uppercase;
}

@media (max-width: 1023px) {
  .app-toolbar {
    padding: 0 1rem;
  }
}
</style>
