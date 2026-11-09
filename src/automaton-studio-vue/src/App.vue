<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import { useUserSettings } from '@/shared/composables/useUserSettings'
import AppToolbar from '@/shared/components/layout/AppToolbar.vue'
import AppSidebar from '@/shared/components/layout/AppSidebar.vue'
import EmailNotConfiguredNotice from '@/features/install/components/EmailNotConfiguredNotice.vue'
import DocumentationDrawer from '@/shared/documentation/components/DocumentationDrawer.vue'
import Toast from 'primevue/toast'
import ConfirmDialog from 'primevue/confirmdialog'

// Initialize settings on app mount
useUserSettings()
const route = useRoute()

// Check if current route uses auth layout (login, register, install, etc.)
const isAuthLayout = computed(() => route.meta.layout === 'auth')

// Check if current route uses no layout (full screen apps, etc.)
const isNoLayout = computed(() => route.meta.layout === 'none')

</script>

<template>
  <!-- Global PrimeVue components -->
  <Toast position="bottom-right" />
  <ConfirmDialog />
  <DocumentationDrawer />

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

        <!-- Page Content -->
        <main class="app-main">
        <EmailNotConfiguredNotice />
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

</style>
