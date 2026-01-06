<script setup lang="ts">
import { computed } from 'vue'
import { useRouter } from 'vue-router'
import { useTheme } from 'vuetify'
import { useAppStore } from '@/shared/stores/app.store'

const router = useRouter()
const theme = useTheme()
const appStore = useAppStore()

// Current page title from route meta
const pageTitle = computed(() => {
  return (router.currentRoute.value.meta?.title as string) || 'Automaton Studio'
})

// Toggle theme and sync with Vuetify
function toggleTheme() {
  appStore.toggleTheme()
  theme.global.name.value = appStore.theme
}
</script>

<template>
  <v-app-bar flat class="border-b">
    <!-- Mobile menu toggle -->
    <v-app-bar-nav-icon class="d-lg-none" @click="appStore.toggleDrawer" />

    <!-- Page Title -->
    <v-app-bar-title class="text-h6 font-weight-medium">
      {{ pageTitle }}
    </v-app-bar-title>

    <v-spacer />

    <!-- Search Bar -->
    <v-text-field
      density="compact"
      variant="solo-filled"
      flat
      hide-details
      placeholder="Search..."
      prepend-inner-icon="mdi-magnify"
      class="mr-4 d-none d-md-flex"
      style="max-width: 300px"
    />

    <!-- Theme Toggle -->
    <v-btn
      :icon="appStore.isDark ? 'mdi-weather-sunny' : 'mdi-weather-night'"
      variant="text"
      @click="toggleTheme"
    >
      <v-tooltip activator="parent" location="bottom">
        {{ appStore.isDark ? 'Light mode' : 'Dark mode' }}
      </v-tooltip>
    </v-btn>

    <!-- Notifications -->
    <v-btn icon variant="text">
      <v-badge color="error" dot>
        <v-icon>mdi-bell-outline</v-icon>
      </v-badge>
      <v-tooltip activator="parent" location="bottom">
        Notifications
      </v-tooltip>
    </v-btn>

    <!-- User Menu -->
    <v-menu>
      <template #activator="{ props }">
        <v-btn v-bind="props" variant="text" class="ml-2">
          <v-avatar size="32" color="primary">
            <span class="text-body-2 font-weight-medium">JD</span>
          </v-avatar>
          <v-icon end size="small">mdi-chevron-down</v-icon>
        </v-btn>
      </template>
      <v-list density="compact" width="200">
        <v-list-item prepend-icon="mdi-account" title="Profile" />
        <v-list-item prepend-icon="mdi-cog" title="Settings" to="/settings" />
        <v-divider class="my-1" />
        <v-list-item prepend-icon="mdi-logout" title="Logout" />
      </v-list>
    </v-menu>
  </v-app-bar>
</template>
