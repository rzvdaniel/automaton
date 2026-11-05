<script setup lang="ts">
import { onUnmounted, ref } from 'vue'
import Dialog from 'primevue/dialog'
import Message from 'primevue/message'
import Textarea from 'primevue/textarea'
import Button from 'primevue/button'
import { isAxiosError } from 'axios'
import { runnersService } from '../services/runners.service'
import type { RunnerUpgradeInstructions, RunnerUpgradeStatus } from '../types/runners.types'

const props = defineProps<{ runnerId: string; runnerName: string; online: boolean; upgradeAvailable: boolean }>()
const emit = defineEmits<{ finished: [] }>()
const visible = defineModel<boolean>('visible', { default: false })

const status = ref<RunnerUpgradeStatus | null>(null)
const error = ref<string | null>(null)
const starting = ref(false)
const instructions = ref<RunnerUpgradeInstructions | null>(null)
const copyError = ref<string | null>(null)
let timer: number | null = null
let deadline = 0

const running = () => starting.value || status.value?.state === 'InProgress'

function stopPolling() {
  if (timer !== null) window.clearTimeout(timer)
  timer = null
}
onUnmounted(stopPolling)

function failure(err: unknown, fallback: string) {
  return isAxiosError(err) ? err.response?.data?.message ?? fallback : fallback
}

async function upgradeNow() {
  error.value = null
  starting.value = true
  try {
    status.value = await runnersService.startUpgrade(props.runnerId)
    deadline = Date.now() + 10 * 60 * 1000
    poll()
  } catch (err) {
    status.value = null
    error.value = failure(err, 'Could not start the upgrade.')
  } finally {
    starting.value = false
  }
}

function poll() {
  stopPolling()
  timer = window.setTimeout(async () => {
    try {
      status.value = await runnersService.getUpgradeStatus(props.runnerId)
    } catch {
      // The Runner is restarting; keep waiting until the deadline.
    }
    if (status.value?.state === 'InProgress' && Date.now() < deadline) return poll()
    if (status.value?.state === 'InProgress')
      status.value = { ...status.value, state: 'Unknown', message: 'The Runner did not report back in 10 minutes. Check the Runners page, or use the manual command.' }
    emit('finished')
  }, 4000)
}

async function showManual() {
  error.value = null
  try { instructions.value = await runnersService.prepareUpgrade(props.runnerId) }
  catch (err) { error.value = failure(err, 'Could not prepare the manual command.') }
}

async function copyCommand() {
  try { await navigator.clipboard.writeText(instructions.value?.upgradeCommand ?? '') }
  catch { copyError.value = 'Select and copy the upgrade command manually.' }
}
</script>

<template>
  <Dialog v-model:visible="visible" modal header="Upgrade Runner" :style="{ width: '48rem', maxWidth: '95vw' }"
    :closable="!running()">
    <Message v-if="!upgradeAvailable && !status" severity="success" :closable="false">
      {{ runnerName }} already uses the server's current Runner image.
    </Message>
    <template v-if="!status || status.state === 'Unknown'">
      <p>
        Upgrade {{ runnerName }} to the server's current Runner image. Deployed apps keep running. The Runner
        restarts and reconnects within a few minutes; if the new container does not connect, the previous one is
        restored. It waits until no deploy or Action is running.
      </p>
      <Message v-if="!online" severity="warn" :closable="false">
        {{ runnerName }} is offline. Use the manual command below.
      </Message>
      <Button label="Upgrade now" icon="pi pi-refresh" :loading="starting" :disabled="!online || !upgradeAvailable"
        data-testid="runner-upgrade-now" @click="upgradeNow" />
    </template>
    <Message v-if="status?.state === 'InProgress'" severity="info" :closable="false" data-testid="runner-upgrade-progress">
      <i class="pi pi-spin pi-spinner mr-2"></i>{{ status.message }}
    </Message>
    <Message v-else-if="status?.state === 'Reconnected'" severity="success" :closable="false" data-testid="runner-upgrade-result">
      {{ runnerName }} reconnected on the new image.
    </Message>
    <Message v-else-if="status?.state === 'RolledBack'" severity="error" :closable="false" data-testid="runner-upgrade-result">
      The upgrade failed and was rolled back. {{ runnerName }} runs on its previous image again.
      <pre class="mt-2 mb-0 text-sm white-space-pre-wrap">{{ status.message }}</pre>
    </Message>
    <Message v-else-if="status?.state === 'Unknown'" severity="warn" :closable="false">{{ status.message }}</Message>
    <Message v-if="error" severity="error" :closable="false" class="mt-3" data-testid="runner-upgrade-error">{{ error }}</Message>

    <h4 class="mt-5 mb-2">Run it manually</h4>
    <p class="mt-0 text-color-secondary">
      For an offline or broken Runner, or one too old to upgrade itself: run this command on the Runner's server.
    </p>
    <Button v-if="!instructions" label="Show command" text @click="showManual" />
    <template v-else>
      <Textarea :model-value="instructions.upgradeCommand" readonly rows="10" class="w-full"
        aria-label="Runner upgrade command" />
      <Button label="Copy command" icon="pi pi-copy" text @click="copyCommand" />
      <Message v-if="copyError" severity="error" :closable="false" class="mt-2">{{ copyError }}</Message>
    </template>
    <template #footer>
      <Button label="Close" text :disabled="running()" @click="visible = false" />
    </template>
  </Dialog>
</template>
