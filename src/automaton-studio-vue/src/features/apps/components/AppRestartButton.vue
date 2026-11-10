<script setup lang="ts">
import { ref } from 'vue'
import Button from 'primevue/button'
import { useToast } from 'primevue/usetoast'
import { appsService } from '../services/apps.service'
import { APP_START_PROGRESS, appActionErrorMessage } from '../utils/app-action-error'

const props = defineProps<{ appId: string }>()
const toast = useToast()
const busy = ref(false)

/** Restarts the running app so it loads its current settings and secrets. */
async function restart() {
  busy.value = true
  try {
    const { restarted } = await appsService.restart(props.appId)
    toast.add({
      severity: restarted > 0 ? 'success' : 'info',
      summary: restarted > 0 ? 'Restarting' : 'Nothing to restart',
      detail: restarted > 0 ? 'The app restarts with its current settings and secrets.' : 'The app is not running. Settings and secrets apply the next time it starts.',
      life: 4000,
    })
  } catch (failure) {
    toast.add({ severity: 'error', summary: 'Could not restart', detail: appActionErrorMessage(failure, 'The app could not be restarted.'), life: 12000 })
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <span class="restart-wrap">
    <Button label="Restart" icon="pi pi-refresh" size="small" severity="secondary" outlined :loading="busy" data-testid="restart-app" @click="restart" />
    <small v-if="busy" class="restart-progress" role="status" data-testid="restart-progress">{{ APP_START_PROGRESS }}</small>
  </span>
</template>

<style scoped>
.restart-wrap { display: inline-flex; align-items: center; gap: 0.5rem; }
.restart-progress { color: var(--p-text-muted-color); }
</style>
