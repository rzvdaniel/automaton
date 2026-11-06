<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import Message from 'primevue/message'
import { appsService } from '../services/apps.service'
import type { InstallableApp } from '../types/apps.types'

const apps = ref<InstallableApp[]>([])
const loading = ref(true)
const error = ref<string | null>(null)

onMounted(async () => {
  try {
    apps.value = await appsService.getInstallableApps()
  } catch {
    error.value = 'The list of installable apps could not be loaded. Try again in a moment.'
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <div class="install-apps flex flex-column gap-4">
    <p class="m-0 text-color-secondary">
      Tools published on this server that run in the background and have no web page, such as backups and checks.
      Install one to run it on your own server.
    </p>
    <Message v-if="error" severity="error" :closable="false">{{ error }}</Message>
    <p v-else-if="loading" class="m-0 text-color-secondary">Loading…</p>
    <p v-else-if="apps.length === 0" class="m-0 text-color-secondary">No installable apps are published on this server yet.</p>
    <ul v-else class="install-apps-list m-0 p-0">
      <li v-for="app in apps" :key="app.id" class="install-apps-row">
        <div class="flex flex-column gap-1">
          <strong>{{ app.name }}</strong>
          <span class="text-color-secondary">{{ app.summary || app.description || 'An installable Automaton app.' }}</span>
          <small v-if="app.creatorDisplayName" class="text-color-secondary">by {{ app.creatorDisplayName }}</small>
        </div>
        <RouterLink :to="{ name: 'app-install', params: { sourceAppId: app.id } }" class="p-button p-component install-apps-button" :aria-label="`Install ${app.name}`">
          Install
        </RouterLink>
      </li>
    </ul>
  </div>
</template>

<style scoped>
.install-apps {
  max-width: 56rem;
}
.install-apps-list {
  list-style: none;
  display: grid;
  gap: 0.75rem;
}
.install-apps-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  padding: 1rem;
  border: 1px solid var(--surface-border);
  border-radius: 12px;
  background: var(--surface-card);
}
.install-apps-button {
  text-decoration: none;
  flex-shrink: 0;
}
</style>
