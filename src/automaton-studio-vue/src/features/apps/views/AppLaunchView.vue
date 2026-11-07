<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import ProgressSpinner from 'primevue/progressspinner'
import { appsService } from '../services/apps.service'
import { getLaunchTargetUrl } from '../utils/public-app-url'

// Where a visitor of an app's private address lands to sign in: after signing in, Studio hands
// out the app's session cookie and sends the visitor back to the page they asked for.
const route = useRoute()
const appId = computed(() => String(route.params.id || ''))
const error = ref('')

onMounted(async () => {
  try {
    await appsService.createRuntimeLaunch(appId.value)
    const to = typeof route.query.to === 'string' ? route.query.to : '/'
    window.location.replace(getLaunchTargetUrl(appId.value, to))
  } catch (err) {
    const status = (err as { response?: { status?: number } }).response?.status
    error.value = status === 403
      ? 'You do not have access to this app. Ask its owner to share it with you.'
      : status === 404
        ? 'This app does not exist.'
        : 'The app could not be opened. Try again in a moment.'
  }
})
</script>

<template>
  <div class="launch-view">
    <div v-if="error" class="launch-view__state" role="alert">
      <i class="pi pi-lock launch-view__icon"></i>
      <p>{{ error }}</p>
      <router-link to="/apps">Back to your apps</router-link>
    </div>
    <div v-else class="launch-view__state">
      <ProgressSpinner stroke-width="4" />
      <p>Opening the app...</p>
    </div>
  </div>
</template>

<style scoped>
.launch-view {
  min-height: 100vh;
  display: flex;
  align-items: center;
  justify-content: center;
  background: var(--p-surface-0, var(--surface-ground));
  color: var(--p-text-color, inherit);
}
.launch-view__state {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.75rem;
  text-align: center;
  padding: 1.5rem;
}
.launch-view__icon {
  font-size: 2rem;
}
</style>
