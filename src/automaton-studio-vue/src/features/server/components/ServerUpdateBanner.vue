<script setup lang="ts">
import { computed, onBeforeUnmount, ref } from 'vue'
import Button from 'primevue/button'
import Dialog from 'primevue/dialog'
import Message from 'primevue/message'
import { installService } from '@/features/install/services/install.service'
import type { InstallationUpdateStatus } from '@/features/install/types/install.types'

const props = defineProps<{ current: string | null; latest: string | null }>()
const emit = defineEmits<{ updated: [] }>()

const pollMilliseconds = 3000
const confirming = ref(false)
const starting = ref(false)
const error = ref<string | null>(null)
const status = ref<InstallationUpdateStatus | null>(null)
// Set from the click until the host reports a run that started later than the one seen before.
const waitingForRun = ref<string | null | undefined>(undefined)
let timer: ReturnType<typeof setTimeout> | undefined

const run = computed(() => status.value?.run ?? null)
const updating = computed(() => waitingForRun.value !== undefined
  || ((run.value?.state === 'running' || run.value?.state === 'starting') && !status.value?.stale))
const failed = computed(() => !updating.value && run.value?.state === 'failed' ? run.value : null)

onBeforeUnmount(() => clearTimeout(timer))

async function poll() {
  try {
    const next = await installService.getUpdate()
    status.value = next
    const started = next.run?.startedAtUtc ?? null
    if (waitingForRun.value !== undefined && next.run && started !== waitingForRun.value) waitingForRun.value = undefined
    if (next.hint) waitingForRun.value = undefined // the host did not pick the request up
    if (!updating.value && next.run?.state === 'succeeded') {
      emit('updated')
      return
    }
  } catch {
    // The API restarts while an update replaces its container; keep polling.
  }
  clearTimeout(timer)
  if (updating.value) timer = setTimeout(poll, pollMilliseconds)
}

async function start() {
  confirming.value = false
  starting.value = true
  error.value = null
  try {
    status.value = await installService.getUpdate()
    waitingForRun.value = status.value.run?.startedAtUtc ?? null
    await installService.startUpdate('latest')
    await poll()
  } catch (caught) {
    waitingForRun.value = undefined
    const response = (caught as { response?: { data?: { message?: string } } }).response
    error.value = response?.data?.message ?? 'The update could not be started.'
  } finally {
    starting.value = false
  }
}
</script>

<template>
  <div class="flex flex-column gap-2" data-testid="server-update">
    <Message v-if="updating" severity="info" :closable="false" data-testid="update-running">
      Updating to {{ props.latest ?? 'the latest release' }}. Studio can be unavailable for a few minutes; this page keeps checking.
    </Message>
    <Message v-else severity="warn" :closable="false" data-testid="update-available">
      <div class="flex align-items-center justify-content-between gap-3 flex-wrap w-full">
        <span>
          <strong>{{ props.latest }}</strong> is available. This server runs {{ props.current ?? 'an unknown version' }}.
        </span>
        <Button label="Update" icon="pi pi-download" size="small" :loading="starting" @click="confirming = true" />
      </div>
    </Message>
    <Message v-if="error" severity="error" :closable="false">{{ error }}</Message>
    <Message v-if="status?.hint" severity="warn" :closable="false">{{ status.hint }}</Message>
    <Message v-if="failed" severity="error" :closable="false" data-testid="update-failed">
      {{ failed.message ?? 'The last update failed.' }} The previous release is kept.
    </Message>
    <Dialog v-model:visible="confirming" modal header="Update the server?" append-to="self" :style="{ width: '32rem', maxWidth: '95vw' }">
      <p class="mt-0">
        Automaton takes a recovery backup, then updates to {{ props.latest }}. Studio and the API can be briefly
        unavailable. If the update fails, the previous release is kept.
      </p>
      <template #footer>
        <Button label="Cancel" text @click="confirming = false" />
        <Button label="Update now" icon="pi pi-check" @click="start" />
      </template>
    </Dialog>
  </div>
</template>
