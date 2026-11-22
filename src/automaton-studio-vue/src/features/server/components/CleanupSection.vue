<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import Button from 'primevue/button'
import Message from 'primevue/message'
import { serverCleanupService } from '../services/server-cleanup.service'
import { formatGiB } from '../utils/format'
import type { ServerCleanup } from '../types/server.types'

const cleanup = ref<ServerCleanup | null>(null)
const loadError = ref<string | null>(null)
defineProps<{ consoleId: string | null; installing?: boolean }>()
const emit = defineEmits<{ install: [] }>()

const lastRun = computed(() => cleanup.value?.runs[0] ?? null)
const failed = computed(() => lastRun.value?.result.toLowerCase() === 'failed')

function freed(bytes: number): string {
  return bytes < 1024 ** 3 ? `${Math.round(bytes / 1024 ** 2)} MB` : formatGiB(bytes)
}

async function load() {
  try {
    const next = await serverCleanupService.get()
    cleanup.value = next
    loadError.value = null
  } catch {
    loadError.value = 'The cleanup could not be loaded.'
  }
}

onMounted(load)
</script>

<template>
  <section class="mb-4" data-testid="cleanup-section">
    <Message v-if="loadError" severity="warn" :closable="false">{{ loadError }}</Message>
    <p v-else-if="cleanup" class="m-0" :class="{ 'text-red-500': failed }" data-testid="cleanup-line">
      <strong>Cleanup:</strong> nightly at {{ cleanup.settings.timeUtc }}
      <template v-if="lastRun">
        &middot; <span data-testid="cleanup-last">{{ failed ? `last run failed: ${lastRun.message ?? lastRun.result}` : `last run freed ${freed(lastRun.bytesFreed)}` }}</span>
      </template>
      <template v-else> &middot; <span data-testid="cleanup-last">no run yet</span></template>
      &middot;
      <RouterLink v-if="consoleId" :to="`/apps/${consoleId}`" data-testid="cleanup-manage">Manage in Server Console</RouterLink>
      <Button
        v-else label="Install Server Console" class="btn-small" :loading="installing"
        data-testid="cleanup-install" @click="emit('install')"
      />
    </p>
  </section>
</template>
