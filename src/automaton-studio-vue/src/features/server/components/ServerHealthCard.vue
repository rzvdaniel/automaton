<script setup lang="ts">
import { computed } from 'vue'
import Button from 'primevue/button'
import CleanupSection from './CleanupSection.vue'
import { formatGiB } from '../utils/format'
import type { ServerHealth } from '../types/server.types'

const props = defineProps<{
  health: ServerHealth | null
  consoleId: string | null
  /** 'updating' while the update replaces the API, 'unreachable' when a refresh failed. */
  paused: 'updating' | 'unreachable' | null
  shownAt: string | null
  showConsole: boolean
  installing: boolean
  installError: string | null
}>()
const emit = defineEmits<{ retry: []; install: [] }>()

const host = computed(() => props.health?.host ?? null)
function usage(total: number | null | undefined, available: number | null | undefined): string {
  return total && available !== null && available !== undefined ? `${formatGiB(total - available)} of ${formatGiB(total)}` : 'Not reported'
}
const memory = computed(() => usage(host.value?.memoryTotalBytes, host.value?.memoryAvailableBytes))
const disk = computed(() => usage(host.value?.diskTotalBytes, host.value?.diskAvailableBytes))
</script>

<template>
  <section class="studio-card health-card" data-testid="health-card">
    <div class="health-card__head">
      <h2 class="studio-section m-0">Health</h2>
      <span v-if="paused === 'updating'" class="studio-muted" data-testid="health-paused">paused while updating</span>
      <span v-else-if="paused === 'unreachable'" class="studio-muted" data-testid="health-paused">
        could not refresh<template v-if="shownAt"> &middot; showing {{ shownAt }} values</template>
        <Button label="Retry" text class="btn-small" @click="emit('retry')" />
      </span>
    </div>
    <ul class="health-list">
      <li class="health-row"><span>Memory</span><span data-testid="health-memory">{{ memory }}</span></li>
      <li class="health-row"><span>Disk</span><span data-testid="health-disk">{{ disk }}</span></li>
      <CleanupSection :console-id="consoleId" />
    </ul>
    <p v-if="showConsole" class="m-0 studio-muted" data-testid="server-console-notice">
      Graphs, OS updates, certificates and maintenance:
      <RouterLink v-if="consoleId" :to="`/apps/${consoleId}`" data-testid="server-console-open">open Server Keeper</RouterLink>
      <Button
        v-else label="Install Server Keeper" text class="btn-small" :loading="installing"
        data-testid="server-console-install" @click="emit('install')"
      />
    </p>
    <p v-if="installError" class="m-0" data-testid="server-console-install-error">{{ installError }}</p>
  </section>
</template>

<style scoped>
.health-card__head { display: flex; align-items: center; justify-content: space-between; flex-wrap: wrap; gap: 8px; }
.health-list { list-style: none; margin: 0; padding: 0; }
.health-list :deep(.health-row) { display: flex; align-items: center; justify-content: space-between; gap: 12px; padding: 10px 0; border-top: 1px solid var(--row-line); }
.health-list :deep(.health-row:first-child) { border-top: 0; }
.health-list :deep(.health-row > :first-child::before) { content: ''; display: inline-block; width: 8px; height: 8px; margin-right: 10px; border-radius: 50%; background: var(--ink-soft); }
.health-list :deep(.health-row > :first-child) { color: var(--ink); }
.health-list :deep(.health-row > :last-child) { color: var(--ink-soft); text-align: right; }
.health-list :deep(.health-row__bad) { color: var(--signal-ink) !important; }
</style>
