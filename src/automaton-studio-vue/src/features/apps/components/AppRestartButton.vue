<script setup lang="ts">
import { ref } from 'vue'
import Button from 'primevue/button'
import { useToast } from 'primevue/usetoast'
import { restartApp } from '../utils/restart-app'
import { APP_START_PROGRESS } from '../utils/app-action-error'

const props = defineProps<{ appId: string }>()
const toast = useToast()
const busy = ref(false)

/** Restarts the running app so it loads its current settings and secrets. */
async function restart() {
  busy.value = true
  try {
    await restartApp(props.appId, toast)
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
