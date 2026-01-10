<script setup lang="ts">
import { computed, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import { useTheme } from '@/shared/composables/useTheme'
import AppToolbar from '@/shared/components/layout/AppToolbar.vue'
import AppSidebar from '@/shared/components/layout/AppSidebar.vue'
import Toast from 'primevue/toast'
import ConfirmDialog from 'primevue/confirmdialog'

// Initialize theme on app mount
const { isDark } = useTheme()
const route = useRoute()

// Check if current route uses auth layout (login, register, install, etc.)
const isAuthLayout = computed(() => route.meta.layout === 'auth')

onMounted(() => {
  // Theme is initialized in useTheme composable
  console.log('App mounted, dark mode:', isDark.value)
})
</script>

<template>
  <!-- Global PrimeVue components -->
  <Toast />
  <ConfirmDialog />

  <!-- Auth layout: full-screen without sidebar/toolbar -->
  <div v-if="isAuthLayout" class="auth-layout">
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
