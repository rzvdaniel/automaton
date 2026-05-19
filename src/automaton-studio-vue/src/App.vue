<script setup lang="ts">
import { computed, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import { useUserSettings } from '@/shared/composables/useUserSettings'
import { useProjectsStore } from '@/features/projects/stores/projectsStore'
import AppToolbar from '@/shared/components/layout/AppToolbar.vue'
import AppSidebar from '@/shared/components/layout/AppSidebar.vue'
import Toast from 'primevue/toast'
import ConfirmDialog from 'primevue/confirmdialog'

// Initialize settings on app mount
useUserSettings()
const projectsStore = useProjectsStore()
const route = useRoute()

// Check if current route uses auth layout (login, register, install, etc.)
const isAuthLayout = computed(() => route.meta.layout === 'auth')

// Check if current route uses no layout (full screen apps, etc.)
const isNoLayout = computed(() => route.meta.layout === 'none')

const shouldInitializeAuthenticatedShell = computed(() => {
  return route.meta.requiresAuth !== false && !isAuthLayout.value && !isNoLayout.value
})

onMounted(async () => {
  // Public no-layout pages such as published apps must not trigger authenticated
  // shell bootstrap requests, or they can redirect anonymous visitors to login.
  if (shouldInitializeAuthenticatedShell.value) {
    await projectsStore.initialize()
  }
})
</script>

<template>
  <!-- Global PrimeVue components -->
  <Toast position="bottom-right" />
  <ConfirmDialog />

  <!-- No layout: full-screen for published apps -->
  <template v-if="isNoLayout">
    <RouterView />
  </template>

  <!-- Auth layout: full-screen without sidebar/toolbar -->
  <div v-else-if="isAuthLayout" class="auth-layout">
    <RouterView />
  </div>

  <!-- Default layout: with sidebar and toolbar -->
  <div v-else class="app-shell">
    <!-- Sidebar / Navigation Rail -->
    <AppSidebar />

    <!-- Main Content Area -->
    <div class="app-content">
      <!-- Top Toolbar -->
      <AppToolbar />

      <div class="preview-banner">
        <span class="preview-banner__label">Developer Preview</span>
        <span class="preview-banner__text">APIs and features may change as the platform evolves.</span>
      </div>

      <!-- Page Content -->
      <main class="app-main">
        <RouterView />
      </main>
    </div>
  </div>
</template>

<style scoped>
/* Layout styles are in main.css for proper dark mode support */

/* Auth layout takes full screen */
.auth-layout {
  min-height: 100vh;
  width: 100%;
}

.preview-banner {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  padding: 0.7rem 1.5rem;
  border-bottom: 1px solid var(--p-surface-200);
  background:
    linear-gradient(90deg, color-mix(in srgb, var(--p-cyan-500) 12%, transparent), transparent 70%),
    var(--p-surface-0);
  color: var(--p-text-color);
}

.preview-banner__label {
  display: inline-flex;
  align-items: center;
  min-height: 1.7rem;
  padding: 0 0.7rem;
  border: 1px solid color-mix(in srgb, var(--p-cyan-500) 34%, transparent);
  border-radius: 999px;
  background: color-mix(in srgb, var(--p-cyan-500) 12%, transparent);
  color: var(--p-cyan-700);
  font-size: 0.72rem;
  font-weight: 800;
  letter-spacing: 0.04em;
  text-transform: uppercase;
}

.preview-banner__text {
  font-size: 0.9rem;
  color: var(--p-text-muted-color);
}

:global(.app-dark) .preview-banner {
  border-bottom-color: var(--p-surface-700);
  background:
    linear-gradient(90deg, color-mix(in srgb, var(--p-cyan-400) 18%, transparent), transparent 70%),
    var(--p-surface-900);
}

:global(.app-dark) .preview-banner__label {
  color: var(--p-cyan-200);
}
</style>
