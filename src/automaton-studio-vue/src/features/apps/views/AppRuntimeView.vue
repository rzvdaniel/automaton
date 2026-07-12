<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import ProgressSpinner from 'primevue/progressspinner'
import { appsService } from '../services/apps.service'
import type { App } from '../types/apps.types'
import { getOpenAppUrl } from '../utils/public-app-url'

const route = useRoute()
const router = useRouter()

const appId = computed(() => String(route.params.id || ''))
const app = ref<App | null>(null)
const applicationUrl = ref('')
const isLoading = ref(true)
const error = ref('')

async function loadRuntimeView() {
  if (!appId.value) {
    error.value = 'App not found.'
    isLoading.value = false
    return
  }

  isLoading.value = true
  error.value = ''

  try {
    app.value = await appsService.get(appId.value)

    if (!(app.value.frontend.runtimeAvailable || app.value.hasDeployedFrontend)) {
      error.value = 'This app has not been deployed yet.'
      return
    }

    const runtimeUrl = app.value.requiresAuthentication
      ? new URL(appsService.getRestrictedRuntimeLaunchUrl(appId.value), window.location.origin)
      : new URL(getOpenAppUrl(app.value), window.location.origin)
    runtimeUrl.searchParams.set('v', Date.now().toString())
    applicationUrl.value = runtimeUrl.toString()
  } catch (err) {
    error.value = err instanceof Error ? err.message : 'Failed to load application.'
  } finally {
    isLoading.value = false
  }
}

onMounted(() => {
  void loadRuntimeView()
})
</script>

<template>
  <div class="app-runtime-view">
    <div v-if="isLoading" class="runtime-state">
      <ProgressSpinner stroke-width="4" />
      <p>Loading application...</p>
    </div>

    <div v-else-if="error" class="runtime-state">
      <i class="pi pi-exclamation-triangle runtime-state__icon"></i>
      <p>{{ error }}</p>
      <div class="runtime-state__actions">
        <button class="runtime-action" type="button" @click="router.push(`/apps/${appId}`)">Back to Studio</button>
        <button class="runtime-action runtime-action--primary" type="button" @click="loadRuntimeView">Retry</button>
      </div>
    </div>

    <template v-else>
      <iframe
        :key="applicationUrl"
        :src="applicationUrl"
        class="runtime-iframe"
        sandbox="allow-scripts allow-same-origin allow-forms allow-modals allow-downloads"
        title="Running Application"
      />
    </template>
  </div>
</template>

<style scoped>
.app-runtime-view {
  display: flex;
  flex-direction: column;
  min-height: 100vh;
  background: var(--p-surface-ground);
}

.runtime-state__actions {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}

.runtime-iframe {
  flex: 1;
  width: 100%;
  min-height: 0;
  border: 0;
  background: white;
}

.runtime-state {
  flex: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 1rem;
  padding: 2rem;
  text-align: center;
}

.runtime-state__icon {
  font-size: 2rem;
  color: var(--p-orange-500);
}

.runtime-action {
  appearance: none;
  border: 1px solid var(--p-surface-300);
  background: var(--p-surface-0);
  color: var(--p-text-color);
  border-radius: 0.5rem;
  padding: 0.625rem 0.9rem;
  cursor: pointer;
}

.runtime-action--primary {
  background: var(--p-primary-500);
  border-color: var(--p-primary-500);
  color: white;
}
</style>
