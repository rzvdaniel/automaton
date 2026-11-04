<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import Button from 'primevue/button'
import Dialog from 'primevue/dialog'
import Message from 'primevue/message'
import { installService } from '../services/install.service'
import type { InstallationUpdateStatus } from '../types/install.types'

const pollMilliseconds = 3000

const status = ref<InstallationUpdateStatus | null>(null)
const loading = ref(true)
const loadError = ref<string | null>(null)
const actionError = ref<string | null>(null)
const confirming = ref(false)
const starting = ref(false)
// Set after a request until the host reports a run that started later than the one shown before.
const waitingForRun = ref<string | null | undefined>(undefined)
const reconnecting = ref(false)
let timer: ReturnType<typeof setTimeout> | undefined

const run = computed(() => status.value?.run ?? null)
const running = computed(() => run.value?.state === 'running' || waitingForRun.value !== undefined)

onMounted(() => load())
onBeforeUnmount(() => clearTimeout(timer))

async function load() {
  try {
    const next = await installService.getUpdate()
    status.value = next
    loadError.value = null
    reconnecting.value = false
    if (waitingForRun.value !== undefined && next.run && next.run.startedAtUtc !== waitingForRun.value) waitingForRun.value = undefined
  } catch {
    // The API restarts while an update replaces its container; keep polling in that case.
    if (running.value) reconnecting.value = true
    else loadError.value = 'Update status could not be loaded.'
  } finally {
    loading.value = false
  }
  clearTimeout(timer)
  if (running.value) timer = setTimeout(load, pollMilliseconds)
}

async function start() {
  confirming.value = false
  starting.value = true
  actionError.value = null
  try {
    waitingForRun.value = status.value?.run?.startedAtUtc ?? null
    await installService.startUpdate('latest')
    await load()
  } catch (error) {
    waitingForRun.value = undefined
    const response = (error as { response?: { data?: { message?: string } } }).response
    actionError.value = response?.data?.message ?? 'The update could not be started.'
  } finally {
    starting.value = false
  }
}

function stateLabel(state: string) {
  return { running: 'Running', succeeded: 'Succeeded', failed: 'Failed', unknown: 'Unknown' }[state] ?? state
}
</script>

<template>
  <div class="flex flex-column gap-3">
    <p class="m-0 text-color-secondary">
      Updating checks the release, takes a recovery backup of the database and keys, then replaces the application
      services. Studio can be unavailable for a few minutes while it runs. The same update is available from the CLI:
      <code>automaton server update --wait</code>.
    </p>
    <Message v-if="loadError" severity="error" :closable="false">{{ loadError }}</Message>
    <template v-else-if="!loading && status">
      <div>Current version: <strong>{{ status.currentVersion ?? 'Unknown' }}</strong></div>
      <Message v-if="!status.triggerAvailable" severity="warn" :closable="false">
        This server cannot update itself from Studio yet. Run <code>sudo bash /opt/automaton-server/update.sh</code> on the
        server once to enable it.
      </Message>
      <Message v-if="actionError" severity="error" :closable="false">{{ actionError }}</Message>
      <Message v-if="reconnecting" severity="info" :closable="false">
        Waiting for the server to restart. This page reconnects automatically.
      </Message>
      <div v-if="run" class="flex flex-column gap-2" data-testid="update-run">
        <div>
          Last update: <strong>{{ stateLabel(run.state) }}</strong>
          <span v-if="run.fromVersion || run.toVersion"> ({{ run.fromVersion ?? '?' }} to {{ run.toVersion ?? '?' }})</span>
          <span v-if="run.finishedAtUtc" class="text-color-secondary"> on {{ new Date(run.finishedAtUtc).toLocaleString() }}</span>
        </div>
        <Message v-if="run.state === 'failed'" severity="error" :closable="false">
          {{ run.message ?? 'The update failed.' }}
          <template v-if="run.recoveryBackup">
            Recovery backup: <code>{{ run.recoveryBackup }}</code>. The previous release is kept; see the installation
            guide for the recovery steps.
          </template>
        </Message>
        <Message v-else-if="run.state === 'succeeded'" severity="success" :closable="false">
          The server runs {{ run.toVersion }}.
          <template v-if="run.recoveryBackup">Recovery backup: <code>{{ run.recoveryBackup }}</code>.</template>
        </Message>
        <pre v-if="status.log" class="m-0 p-3 surface-ground border-round overflow-auto text-sm" style="max-height: 16rem">{{ status.log }}</pre>
      </div>
      <div>
        <Button
          label="Update to the latest release" icon="pi pi-download" :loading="starting || running"
          :disabled="!status.triggerAvailable || running" @click="confirming = true"
        />
      </div>
    </template>
    <Dialog v-model:visible="confirming" modal header="Update the server?" append-to="self" :style="{ width: '32rem' }">
      <p class="mt-0">
        Automaton takes a recovery backup, then updates to the latest published release. Studio and the API can be
        briefly unavailable. If the update fails, the previous release is kept.
      </p>
      <template #footer>
        <Button label="Cancel" text @click="confirming = false" />
        <Button label="Update now" icon="pi pi-check" @click="start" />
      </template>
    </Dialog>
  </div>
</template>
