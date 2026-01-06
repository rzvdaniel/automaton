<script setup lang="ts">
import { onMounted } from 'vue'
import { useTheme } from '@/shared/composables/useTheme'
import AppToolbar from '@/shared/components/layout/AppToolbar.vue'
import AppSidebar from '@/shared/components/layout/AppSidebar.vue'

// Initialize theme on app mount
const { isDark } = useTheme()

onMounted(() => {
  // Theme is initialized in useTheme composable
  console.log('App mounted, dark mode:', isDark.value)
})
</script>

<template>
  <div class="app-shell">
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
</style>
