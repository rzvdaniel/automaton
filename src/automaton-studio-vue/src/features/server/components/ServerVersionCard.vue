<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import Button from 'primevue/button'
import Dialog from 'primevue/dialog'
import { installService } from '@/features/install/services/install.service'
import type { InstallationUpdateStatus } from '@/features/install/types/install.types'

const props = defineProps<{ current: string | null; latest: string | null; updateAvailable: boolean; checkedAt: string | null }>()
const emit = defineEmits<{ updated: []; updating: [value: boolean]; check: [] }>()

const pollMilliseconds = 3000
const steps = [
  { key: 'backup', label: 'Backup' },
  { key: 'download', label: 'Download' },
  { key: 'restart', label: 'Restart services' },
  { key: 'health', label: 'Health check' },
] as const
const releaseNotes = 'https://github.com/rzvdaniel/automaton/releases'

const confirming = ref(false)
const starting = ref(false)
const error = ref<string | null>(null)
const dismissed = ref(false)
const done = ref(false) // an update seen by this page finished; hide "available" even if health still reports the old version
const watching = ref(false) // an update started here, or was already running when the page opened
const status = ref<InstallationUpdateStatus | null>(null)
// Set from the click until the host reports a run that started later than the one seen before.
const waitingForRun = ref<string | null | undefined>(undefined)
const now = ref(Date.now())
let timer: ReturnType<typeof setTimeout> | undefined
let clock: ReturnType<typeof setInterval> | undefined

const run = computed(() => status.value?.run ?? null)
const updating = computed(() => waitingForRun.value !== undefined
  || ((run.value?.state === 'running' || run.value?.state === 'starting') && !status.value?.stale))
const failed = computed(() => !updating.value && run.value?.state === 'failed' ? run.value : null)
const stepIndex = computed(() => Math.max(0, steps.findIndex((step) => step.key === run.value?.step)))
const failedStep = computed(() => steps.find((step) => step.key === failed.value?.step)?.label ?? null)
const updatedTo = computed(() => run.value?.toVersion ?? props.latest ?? props.current)
const target = computed(() => run.value?.requestedVersion && run.value.requestedVersion !== 'latest' ? run.value.requestedVersion : props.latest)
const plain = (version: string | null | undefined) => version?.replace(/^server-v/, '') ?? null
const checked = computed(() => props.checkedAt ? new Date(props.checkedAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }) : null)

function duration(from: string | null | undefined, to: number): string | null {
  if (!from) return null
  const seconds = Math.max(0, Math.round((to - new Date(from).getTime()) / 1000))
  return seconds < 60 ? `${seconds} s` : `${Math.floor(seconds / 60)} min ${seconds % 60} s`
}
const elapsed = computed(() => duration(run.value?.startedAtUtc, now.value))
const updatedLine = computed(() => {
  const finished = run.value?.finishedAtUtc
  const time = finished ? new Date(finished).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }) : null
  const took = finished ? duration(run.value?.startedAtUtc, new Date(finished).getTime()) : null
  return `Automaton updated to ${plain(updatedTo.value)}${time ? ` at ${time}` : ''}${took ? `, took ${took}` : ''}`
})

watch(updating, (value) => emit('updating', value))
onMounted(() => {
  clock = setInterval(() => { now.value = Date.now() }, 1000)
  void poll(true)
})
onBeforeUnmount(() => { clearTimeout(timer); clearInterval(clock) })

async function poll(initial = false) {
  try {
    const next = await installService.getUpdate()
    status.value = next
    const started = next.run?.startedAtUtc ?? null
    if (waitingForRun.value !== undefined && next.run && started !== waitingForRun.value) waitingForRun.value = undefined
    if (next.hint) waitingForRun.value = undefined // the host did not pick the request up
    if (initial && updating.value) watching.value = true
    if (!updating.value && watching.value && next.run?.state === 'succeeded') {
      done.value = true
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
    watching.value = true
    done.value = false
    dismissed.value = false
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

function downloadLog() {
  const url = URL.createObjectURL(new Blob([status.value?.log ?? ''], { type: 'text/plain' }))
  const link = document.createElement('a')
  link.href = url
  link.download = 'automaton-update.log'
  link.click()
  URL.revokeObjectURL(url)
}
</script>

<template>
  <section class="studio-card version-card" data-testid="version-card">
    <div class="version-card__head">
      <div class="version-card__title">
        <strong class="version-card__version">{{ updating && run ? `Updating Automaton ${plain(run.fromVersion ?? props.current) ?? 'Unknown'} → ${plain(target) ?? 'latest'}` : `Automaton ${props.current ?? 'Unknown version'}` }}</strong>
        <span v-if="updating" class="studio-pill version-card__pill--progress" data-testid="update-running-pill">Updating</span>
        <span v-else-if="failed" class="studio-pill studio-pill--warn">Update failed</span>
        <span v-else-if="!done && props.updateAvailable" class="studio-pill studio-pill--warn" data-testid="update-available-pill">{{ props.latest }} available</span>
        <span v-else class="studio-pill">Up to date</span>
      </div>
      <div v-if="!updating" class="version-card__actions">
        <a :href="releaseNotes" target="_blank" rel="noopener" class="version-card__link">{{ !done && props.updateAvailable ? "What's new" : 'Release notes' }}</a>
        <Button v-if="!props.updateAvailable || done" label="Check now" text class="btn-small" data-testid="check-now" @click="emit('check')" />
      </div>
    </div>

    <div v-if="updating" data-testid="update-running">
      <p class="m-0 studio-muted">
        <template v-if="run?.requestedBy">Started by {{ run.requestedBy }} &middot; </template>
        <template v-if="elapsed">{{ elapsed }} &middot; </template>Studio can be unavailable for a few minutes; this page keeps checking.
      </p>
      <ol class="version-card__steps" data-testid="update-steps">
        <li
          v-for="(step, index) in steps" :key="step.key" :data-step="step.key"
          :class="{ 'is-current': run?.step ? index === stepIndex : index === 0, 'is-done': run?.step ? index < stepIndex : false }"
        >{{ step.label }}</li>
      </ol>
      <div class="version-card__bar"><span :style="{ width: `${((run?.step ? stepIndex : 0) + 0.5) * 25}%` }" /></div>
    </div>

    <div v-else-if="failed" data-testid="update-failed">
      <p class="m-0">
        <template v-if="failedStep">Stopped at <strong>{{ failedStep }}</strong>. </template>{{ failed.message ?? 'The last update failed.' }}
        This server still runs {{ props.current ?? 'the previous release' }} and is healthy.
      </p>
      <div class="version-card__actions mt-2">
        <Button label="Try again" class="btn-small" :loading="starting" data-testid="update-retry" @click="start" />
        <Button label="Download log" outlined class="btn-small" :disabled="!status?.log" @click="downloadLog" />
      </div>
    </div>

    <p v-else-if="done && !dismissed" class="m-0 version-card__done" data-testid="update-succeeded">
      <span>{{ updatedLine }}.</span>
      <Button icon="pi pi-times" text rounded size="small" aria-label="Dismiss" @click="dismissed = true" />
    </p>

    <div v-else-if="!done && props.updateAvailable" data-testid="update-available">
      <Button :label="`Update Automaton to ${props.latest}`" icon="pi pi-download" class="btn-small" :loading="starting" @click="confirming = true" />
      <p class="studio-muted mt-2 mb-0">Takes about 2 minutes. A backup is taken first and apps keep running.</p>
    </div>

    <p v-else class="studio-muted m-0">
      <template v-if="checked">Last checked {{ checked }}</template><template v-else>This is the latest release</template>
    </p>

    <p v-if="error" class="m-0" data-testid="update-start-error">{{ error }}</p>
    <p v-if="status?.hint" class="m-0">{{ status.hint }}</p>

    <Dialog v-model:visible="confirming" modal header="Update Automaton?" append-to="self" :style="{ width: '32rem', maxWidth: '95vw' }">
      <p class="mt-0">
        Automaton takes a recovery backup, then updates to {{ props.latest }}. Studio and the API can be briefly
        unavailable. If the update fails, the previous release is kept.
      </p>
      <template #footer>
        <Button label="Cancel" text @click="confirming = false" />
        <Button label="Update now" icon="pi pi-check" @click="start" />
      </template>
    </Dialog>
  </section>
</template>

<style scoped>
.version-card { gap: 12px; }
.version-card__head { display: flex; align-items: center; justify-content: space-between; flex-wrap: wrap; gap: 8px; }
.version-card__title { display: flex; align-items: center; gap: 10px; flex-wrap: wrap; }
.version-card__version { font-family: var(--display); font-size: 18px; }
.version-card__actions { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; }
.version-card__link { color: var(--ink-soft); font-size: 13px; }
.version-card__pill--progress { border-color: var(--signal); color: var(--signal-ink); }
.version-card__steps { display: flex; flex-wrap: wrap; gap: 8px; margin: 12px 0; padding: 0; list-style: none; }
.version-card__steps li { padding: 4px 12px; border: 1px solid var(--line); border-radius: 999px; font-size: 13px; color: var(--ink-soft); }
.version-card__steps li.is-done { color: var(--ink); }
.version-card__steps li.is-current { border-color: var(--signal); color: var(--signal-ink); font-weight: 600; }
.version-card__bar { height: 6px; border-radius: 999px; background: var(--line); overflow: hidden; }
.version-card__bar span { display: block; height: 100%; background: var(--signal); transition: width 0.4s; }
.version-card__done { display: flex; align-items: center; justify-content: space-between; gap: 8px; }
</style>
