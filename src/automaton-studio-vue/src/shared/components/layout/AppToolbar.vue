<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import { useUserSettings } from '@/shared/composables/useUserSettings'
import Button from 'primevue/button'
import { RouterLink } from 'vue-router'
import { useAppEditorStore } from '@/features/apps/stores/app-editor.store'
import { crumbsForRoute } from './breadcrumb'

const { isDark, toggleDarkMode } = useUserSettings()
const route = useRoute()

const editor = useAppEditorStore()

const crumbs = computed(() => crumbsForRoute(route.name as string | undefined, {
  params: route.params,
  query: route.query,
  meta: route.meta.title,
  appName: editor.currentApp?.id === route.params.id ? editor.currentApp?.name : undefined,
}))
</script>

<template>
  <header class="app-toolbar">
    <nav v-if="crumbs.length > 0" class="toolbar-crumbs" aria-label="Breadcrumb">
      <ol>
        <li v-for="(crumb, index) in crumbs" :key="index">
          <RouterLink v-if="crumb.to" :to="crumb.to">{{ crumb.label }}</RouterLink>
          <span v-else aria-current="page">{{ crumb.label }}</span>
        </li>
      </ol>
    </nav>
    <span v-else />
    <div class="toolbar-end">
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

.toolbar-crumbs ol { display: flex; flex-wrap: wrap; align-items: center; margin: 0; padding: 0; list-style: none; font: 600 0.9rem var(--display); color: var(--ink-soft); }
.toolbar-crumbs li + li::before { content: '›'; margin: 0 0.5rem; }
.toolbar-crumbs a { color: inherit; text-decoration: none; }
.toolbar-crumbs a:hover { color: var(--ink); text-decoration: underline; }
.toolbar-crumbs [aria-current] { color: var(--ink); }

.toolbar-end {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}


@media (max-width: 1023px) {
  .app-toolbar {
    padding: 0 1rem;
  }
}
</style>
