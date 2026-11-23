<script setup lang="ts">
import { computed, ref } from 'vue'
import Button from 'primevue/button'
import { isAxiosError } from 'axios'
import { useConfirm } from 'primevue/useconfirm'
import { useToast } from 'primevue/usetoast'
import { useAuthStore } from '@/features/auth/stores/auth.store'
import RunnerRemovalDialog from '@/features/runners/components/RunnerRemovalDialog.vue'
import RunnerUpgradeDialog from '@/features/runners/components/RunnerUpgradeDialog.vue'
import { runnersService } from '@/features/runners/services/runners.service'
import {
  RunnerStatus,
  isRunnerLinkedToProject,
  type Runner,
  type RunnerRemovalInstructions,
} from '@/features/runners/types/runners.types'

const props = defineProps<{ runners: Runner[] }>()
const emit = defineEmits<{ changed: [] }>()

const toast = useToast()
const confirm = useConfirm()
const removal = ref<RunnerRemovalInstructions | null>(null)
const removalName = ref('')
const showRemoval = ref(false)
const upgrading = ref<Runner | null>(null)
const showUpgrade = ref(false)

type Filter = 'all' | 'online' | 'offline' | 'upgrade'
const filter = ref<Filter>('all')
const search = ref('')
const sortAscending = ref(true)
// Runners a bulk upgrade started; shown as "Upgrading" until the list says they no longer need it.
const upgradingIds = ref<Set<string>>(new Set())
const bulkRunning = ref(false)

const needsUpgrade = (runner: Runner) => !!runner.upgradeAvailable && runner.status === RunnerStatus.Online
type RunnerState = 'Online' | 'Needs upgrade' | 'Offline' | 'Upgrading'
function stateOf(runner: Runner): RunnerState {
  if (upgradingIds.value.has(runner.id) && runner.upgradeAvailable) return 'Upgrading'
  if (runner.status !== RunnerStatus.Online) return 'Offline'
  return runner.upgradeAvailable ? 'Needs upgrade' : 'Online'
}
const counts = computed(() => ({
  online: props.runners.filter((runner) => runner.status === RunnerStatus.Online).length,
  offline: props.runners.filter((runner) => runner.status !== RunnerStatus.Online).length,
  upgrade: props.runners.filter((runner) => !!runner.upgradeAvailable).length,
}))
const upgradable = computed(() => props.runners.filter((runner) => canUpgrade(runner) && needsUpgrade(runner) && !upgradingIds.value.has(runner.id)))
const summary = computed(() => {
  const parts = [`${props.runners.length} ${props.runners.length === 1 ? 'runner' : 'runners'}`, `${counts.value.online} online`]
  if (counts.value.offline) parts.push(`${counts.value.offline} offline`)
  if (counts.value.upgrade) parts.push(`${counts.value.upgrade} need upgrade`)
  return parts.join(' · ')
})
const filtered = computed(() => {
  const term = search.value.trim().toLowerCase()
  return props.runners
    .filter((runner) => filter.value === 'all'
      || (filter.value === 'online' && runner.status === RunnerStatus.Online)
      || (filter.value === 'offline' && runner.status !== RunnerStatus.Online)
      || (filter.value === 'upgrade' && !!runner.upgradeAvailable))
    .filter((runner) => !term || runner.name.toLowerCase().includes(term))
    .sort((a, b) => a.name.localeCompare(b.name) * (sortAscending.value ? 1 : -1))
})
const chips = computed(() => [
  { key: 'all' as const, label: 'All', count: props.runners.length },
  { key: 'online' as const, label: 'Online', count: counts.value.online },
  { key: 'offline' as const, label: 'Offline', count: counts.value.offline },
  { key: 'upgrade' as const, label: 'Needs upgrade', count: counts.value.upgrade },
])

function confirmUpgradeAll() {
  confirm.require({
    message: `Upgrade ${upgradable.value.length} runners to this server's version? Each restarts briefly; its apps keep their data.`,
    header: 'Upgrade runners',
    icon: 'pi pi-refresh',
    rejectLabel: 'Cancel',
    acceptLabel: `Upgrade ${upgradable.value.length} runners`,
    accept: () => void upgradeAll(),
  })
}

async function upgradeAll() {
  bulkRunning.value = true
  const failedNames: string[] = []
  for (const runner of upgradable.value) {
    try {
      await runnersService.startUpgrade(runner.id)
      upgradingIds.value = new Set(upgradingIds.value).add(runner.id)
    } catch {
      failedNames.push(runner.name)
    }
  }
  bulkRunning.value = false
  if (failedNames.length) toast.add({ severity: 'warn', summary: 'Some upgrades did not start', detail: failedNames.join(', '), life: 10000 })
  emit('changed')
}

// Only a Runner installed with Studio's command can be upgraded or removed from here.
const canUpgrade = (runner: Runner) => !runner.isHostedRunner && runner.applicationProfile === 'MachineManagement'

function openUpgrade(runner: Runner) {
  upgrading.value = runner
  showUpgrade.value = true
}

function confirmRemoval(runner: Runner) {
  confirm.require({
    message: `Prepare removal of "${runner.name}"? All app environments must be moved or deleted first. New assignments will be blocked, and you will receive a command to uninstall it on its server.`,
    header: 'Delete Runner',
    icon: 'pi pi-exclamation-triangle',
    rejectLabel: 'Cancel',
    acceptLabel: 'Prepare removal',
    acceptClass: 'p-button-danger',
    accept: () => void prepareRemoval(runner),
  })
}

const isAdmin = computed(() => (useAuthStore().user?.roles ?? []).some((role) => role === 'Admin' || role === 'SysAdmin'))

// An administrator can remove a Runner that is offline for good together with its apps, without cleaning up its machine.
function confirmOfflineRemoval(runner: Runner) {
  confirm.require({
    message: `Delete "${runner.name}" and every app assigned to it? The Runner is offline, so nothing is cleaned up on its machine: containers and files may remain there. This cannot be undone.`,
    header: 'Delete offline Runner and its apps',
    icon: 'pi pi-exclamation-triangle',
    rejectLabel: 'Cancel',
    acceptLabel: 'Delete without cleanup',
    acceptClass: 'p-button-danger',
    accept: () => void deleteOffline(runner),
  })
}

async function deleteOffline(runner: Runner) {
  try {
    const skipped = await runnersService.deleteOffline(runner.id)
    toast.add({
      severity: 'warn',
      summary: 'Runner deleted',
      detail: skipped.length ? `Skipped cleanup: ${skipped.join(' ')}` : `"${runner.name}" has been deleted`,
      life: 15000,
    })
    emit('changed')
  } catch (error) {
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: isAxiosError(error) ? error.response?.data?.message ?? 'Failed to delete the Runner' : 'Failed to delete the Runner',
      life: 10000,
    })
  }
}

async function prepareRemoval(runner: Runner) {
  try {
    removal.value = await runnersService.prepareRemoval(runner.id)
    removalName.value = runner.name
    showRemoval.value = true
    emit('changed')
  } catch (error) {
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: isAxiosError(error) ? error.response?.data?.message ?? 'Failed to prepare Runner removal' : 'Failed to prepare Runner removal',
      life: 10000,
    })
  }
}
</script>

<template>
  <div data-testid="runners-list">
    <div class="runners-bar">
      <span class="studio-muted" data-testid="runners-summary">{{ summary }}</span>
      <Button
        v-if="upgradable.length > 1" :label="`Upgrade ${upgradable.length} runners`" class="btn-small" :loading="bulkRunning"
        data-testid="runners-upgrade-all" @click="confirmUpgradeAll"
      />
    </div>
    <div v-if="runners.length > 1" class="runners-tools">
      <div class="runners-chips">
        <button
          v-for="chip in chips" :key="chip.key" type="button" class="runners-chip" :class="{ 'is-active': filter === chip.key }"
          :data-testid="`runners-filter-${chip.key}`" @click="filter = chip.key"
        >{{ chip.label }} {{ chip.count }}</button>
      </div>
      <input v-model="search" class="runners-search" type="search" placeholder="Search runners" aria-label="Search runners" data-testid="runners-search">
    </div>
    <table class="runners-table">
      <thead>
        <tr>
          <th><button type="button" class="runners-sort" @click="sortAscending = !sortAscending">Name {{ sortAscending ? '↑' : '↓' }}</button></th>
          <th>State</th><th>OS</th><th class="is-number">Apps</th><th>Version</th><th />
        </tr>
      </thead>
      <tbody>
        <tr v-for="runner in filtered" :key="runner.id" data-testid="runner-row">
          <td class="font-semibold">
            {{ runner.name }}
            <span v-if="isRunnerLinkedToProject(runner)" class="studio-muted font-normal"> &middot; Project: {{ runner.projectName }}</span>
          </td>
          <td>
            <span class="runner-state" :class="{ 'is-orange': stateOf(runner) !== 'Online' }" data-testid="runner-state">
              <span class="runner-state__dot" />{{ stateOf(runner) }}
            </span>
          </td>
          <td data-testid="runner-os">{{ runner.operatingSystem || 'Unknown' }}</td>
          <td class="is-number" data-testid="runner-apps">{{ runner.appCount ?? '-' }}</td>
          <td data-testid="runner-version">{{ runner.version || 'Unknown' }}</td>
          <td class="runner-actions">
            <Button
              v-if="canUpgrade(runner) && runner.upgradeAvailable && stateOf(runner) !== 'Upgrading'" label="Upgrade" text size="small"
              data-testid="runner-upgrade-button" @click="openUpgrade(runner)"
            />
            <Button
              v-if="isAdmin && runner.status !== RunnerStatus.Online && !runner.isHostedRunner && !isRunnerLinkedToProject(runner)"
              label="Delete with apps…" text size="small" class="runner-remove" data-testid="runner-delete-offline-button"
              @click="confirmOfflineRemoval(runner)"
            />
            <Button
              v-if="!runner.isHostedRunner && !isRunnerLinkedToProject(runner)" label="Remove…" text size="small" class="runner-remove"
              data-testid="runner-remove-button" @click="confirmRemoval(runner)"
            />
          </td>
        </tr>
        <tr v-if="filtered.length === 0"><td colspan="6" class="studio-muted" data-testid="runners-no-match">No runners match.</td></tr>
      </tbody>
    </table>

    <RunnerRemovalDialog
      v-if="removal" :key="removal.runnerId" v-model:visible="showRemoval" :instructions="removal"
      :runner-name="removalName" @removed="emit('changed')"
    />
    <RunnerUpgradeDialog
      v-if="upgrading" :key="upgrading.id" v-model:visible="showUpgrade" :runner-id="upgrading.id"
      :runner-name="upgrading.name" :online="upgrading.status === RunnerStatus.Online"
      :upgrade-available="!!upgrading.upgradeAvailable" @finished="emit('changed')"
    />
  </div>
</template>

<style scoped>
.runners-bar, .runners-tools { display: flex; align-items: center; justify-content: space-between; flex-wrap: wrap; gap: 8px; margin-bottom: 8px; }
.runners-chips { display: flex; gap: 6px; flex-wrap: wrap; }
.runners-chip { padding: 3px 10px; border: 1px solid var(--line); border-radius: 999px; background: var(--paper); color: var(--ink-soft); font: inherit; font-size: 13px; cursor: pointer; }
.runners-chip.is-active { border-color: var(--signal); color: var(--signal-ink); }
.runners-search { padding: 5px 10px; border: 1px solid var(--line); border-radius: 8px; background: var(--paper); color: var(--ink); font: inherit; font-size: 13px; }
.runners-table { width: 100%; border-collapse: collapse; }
.runners-table th { padding: 6px 8px; text-align: left; font-size: 12px; font-weight: 400; color: var(--ink-soft); border-bottom: 1px solid var(--line); }
.runners-table td { padding: 10px 8px; border-bottom: 1px solid var(--row-line); overflow-wrap: anywhere; }
.runners-table .is-number { text-align: right; font-variant-numeric: tabular-nums; }
.runners-sort { padding: 0; border: 0; background: none; color: inherit; font: inherit; cursor: pointer; }
.runner-state { display: inline-flex; align-items: center; gap: 8px; color: var(--ink-soft); }
.runner-state.is-orange { color: var(--signal-ink); }
.runner-state__dot { width: 8px; height: 8px; border-radius: 50%; background: var(--ink-soft); }
.is-orange .runner-state__dot { background: var(--signal); }
.runner-actions { white-space: nowrap; text-align: right; }
.runner-remove, .runner-remove :deep(.p-button-label) { color: #b42318; }
</style>
