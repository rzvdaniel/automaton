<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { serverCleanupService } from '../services/server-cleanup.service'
import { formatGiB } from '../utils/format'
import type { ServerCleanup } from '../types/server.types'

const cleanup = ref<ServerCleanup | null>(null)
const loadError = ref<string | null>(null)
defineProps<{ consoleId: string | null }>()

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
    loadError.value = 'unavailable'
  }
}

onMounted(load)
</script>

<template>
  <li class="health-row" data-testid="cleanup-section">
    <span>Cleanup</span>
    <span v-if="loadError" class="studio-muted">Not available right now</span>
    <span v-else-if="cleanup" :class="failed ? 'health-row__bad' : 'studio-muted'" data-testid="cleanup-line">
      nightly at {{ cleanup.settings.timeUtc }}
      <template v-if="lastRun">
        &middot; <span data-testid="cleanup-last">{{ failed ? `last run failed: ${lastRun.message ?? lastRun.result}` : `last run freed ${freed(lastRun.bytesFreed)}` }}</span>
      </template>
      <template v-else> &middot; <span data-testid="cleanup-last">no run yet</span></template>
      <template v-if="consoleId">
        &middot; <RouterLink :to="`/apps/${consoleId}`" data-testid="cleanup-manage">Manage in Server Console</RouterLink>
      </template>
    </span>
  </li>
</template>
