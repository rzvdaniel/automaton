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
/** Free space a server update needs on the main disk. */
const UPDATE_NEEDS_BYTES = 10 * 1024 ** 3
const DISK_WARNING_PERCENT = 80
const disks = computed(() => {
  const list = host.value?.disks
  const reported = list?.length ? list : [{ mountPoint: '/', totalBytes: host.value?.diskTotalBytes ?? 0, availableBytes: host.value?.diskAvailableBytes ?? 0 }]
  const single = !list?.length
  return reported.map((disk) => {
    const known = disk.totalBytes > 0 && !(single && host.value?.diskAvailableBytes == null)
    const low = known && ((disk.totalBytes - disk.availableBytes) / disk.totalBytes * 100 >= DISK_WARNING_PERCENT
      || (disk.mountPoint === '/' && disk.availableBytes < UPDATE_NEEDS_BYTES))
    const main = disk.mountPoint === '/'
    const label = main ? 'Disk (main, /)' : `Disk (Docker, ${disk.mountPoint})`
    const text = known ? `${usage(disk.totalBytes, disk.availableBytes)} · ${formatGiB(disk.availableBytes)} free` : 'Not reported'
    return { key: disk.mountPoint, label: single ? 'Disk' : label, text, low, note: main && known ? `an update needs ${formatGiB(UPDATE_NEEDS_BYTES)} free` : null }
  })
})
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
      <li v-for="d in disks" :key="d.key" class="health-row">
        <span>{{ d.label }}</span>
        <span data-testid="health-disk" :class="{ 'health-row__bad': d.low }">
          {{ d.text }}<template v-if="d.low"> &middot; low</template><template v-if="d.note"> &middot; {{ d.note }}</template>
        </span>
      </li>
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
