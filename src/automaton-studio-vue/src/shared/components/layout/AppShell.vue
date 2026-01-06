<script setup lang="ts">
import { useTheme } from 'vuetify'
import AppBar from './AppBar.vue'
import NavigationRail from './NavigationRail.vue'
import { useAppStore } from '@/shared/stores/app.store'

const theme = useTheme()
const appStore = useAppStore()

// Sync theme with store
theme.global.name.value = appStore.theme
</script>

<template>
  <v-app :theme="appStore.theme">
    <!-- Navigation Rail (Left Side) -->
    <NavigationRail />

    <!-- App Bar (Top) -->
    <AppBar />

    <!-- Main Content -->
    <v-main class="bg-background">
      <v-container fluid class="pa-6">
        <router-view v-slot="{ Component }">
          <transition name="fade" mode="out-in">
            <component :is="Component" />
          </transition>
        </router-view>
      </v-container>
    </v-main>

    <!-- Global Snackbar -->
    <v-snackbar
      v-model="appStore.snackbar.show"
      :color="appStore.snackbar.color"
      :timeout="appStore.snackbar.timeout"
      location="bottom right"
    >
      {{ appStore.snackbar.message }}
      <template #actions>
        <v-btn variant="text" @click="appStore.hideSnackbar">
          Close
        </v-btn>
      </template>
    </v-snackbar>
  </v-app>
</template>

<style scoped>
.fade-enter-active,
.fade-leave-active {
  transition: opacity 0.2s ease;
}

.fade-enter-from,
.fade-leave-to {
  opacity: 0;
}
</style>
