<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import Button from 'primevue/button'
import Dialog from 'primevue/dialog'
import ProgressSpinner from 'primevue/progressspinner'
import { useToast } from 'primevue/usetoast'
import { applicationEventsService } from '@/shared/services/api/application-events.service'
import type { ApplicationEvent } from '@/shared/types/ai.types'
import { actionSchedulesService } from '../services/action-schedules.service'
import { appsService, type AppActionExecution } from '../services/apps.service'
import type { AppDeploymentTarget, SecretAuditEntry } from '../types/apps.types'
import type { ConsoleEntry } from '../types/console.types'
import { buildActivity, deployActivity, filterActivity, formatActivityTime, type ActivityFilter } from '../utils/app-activity'
import { composeExecutionOutput } from '../utils/execution-output'

const props = defineProps<{
  appId: string
  executions: ReadonlyArray<AppActionExecution>
  /** Web apps show a Live output link while they run. */
  liveOutputAvailable: boolean
  liveLogs: ReadonlyArray<ConsoleEntry>
  refreshKey?: number
  deployTarget?: AppDeploymentTarget | null
  /** Names of the app's declared settings, which are saved like secrets but are not secrets. */
  settingNames?: ReadonlyArray<string>
}>()

const events = ref<ApplicationEvent[]>([])
const secretHistory = ref<SecretAuditEntry[]>([])
const loading = ref(true)
const failed = ref(false)
const filter = ref<ActivityFilter>('all')
const toast = useToast()
/** The run whose output is open (one at a time), the output already loaded, and the one loading. */
const openRun = ref<string | null>(null)
const outputs = ref<Record<string, string>>({})
const outputErrors = ref<Record<string, boolean>>({})
const loadingRun = ref<string | null>(null)
const liveOpen = ref(false)

async function load() {
  failed.value = false
  const id = props.appId
  const [eventPage, secrets] = await Promise.allSettled([
    applicationEventsService.getEvents(id, { pageSize: 100 }),
    appsService.listSecretHistory(id),
  ])
  if (id !== props.appId) return
  events.value = eventPage.status === 'fulfilled' ? eventPage.value.items : []
  secretHistory.value = secrets.status === 'fulfilled' ? secrets.value : []
  failed.value = eventPage.status === 'rejected'
  loading.value = false
}

watch(() => [props.appId, props.refreshKey], load, { immediate: true })

const all = computed(() => buildActivity(props.executions, events.value, secretHistory.value, deployActivity(props.deployTarget ?? null), props.settingNames ?? []))
const visible = computed(() => filterActivity(all.value, filter.value))
const problemCount = computed(() => filterActivity(all.value, 'problems').length)
const hasRuns = computed(() => props.executions.length > 0)

const filters = computed<Array<{ key: ActivityFilter; label: string }>>(() => [
  { key: 'all', label: 'All' },
  ...(hasRuns.value ? [{ key: 'runs' as const, label: 'Runs' }] : []),
  { key: 'changes', label: 'Changes' },
  { key: 'problems', label: problemCount.value > 0 ? `Problems · ${problemCount.value}` : 'Problems' },
])

const liveLines = computed(() => props.liveLogs.map((entry) => entry.result?.output ?? '').filter(Boolean).reverse())

async function toggleRun(executionId: string) {
  if (openRun.value === executionId) {
    openRun.value = null
    return
  }
  openRun.value = executionId
  if (outputs.value[executionId] !== undefined) return
  loadingRun.value = executionId
  outputErrors.value[executionId] = false
  try {
    const [logs, result] = await Promise.all([
      actionSchedulesService.getLogs(props.appId, executionId),
      actionSchedulesService.getResult(props.appId, executionId).catch(() => null),
    ])
    outputs.value[executionId] = composeExecutionOutput(logs, result)
  } catch {
    outputErrors.value[executionId] = true
  } finally {
    if (loadingRun.value === executionId) loadingRun.value = null
  }
}

async function copyOutput(executionId: string) {
  try {
    await navigator.clipboard.writeText(outputs.value[executionId] ?? '')
    toast.add({ severity: 'success', summary: 'Output copied', life: 2000 })
  } catch {
    toast.add({ severity: 'warn', summary: 'Could not copy the output', life: 3000 })
  }
}
</script>

<template>
  <div class="activity">
    <div class="chips" role="group" aria-label="Filter activity">
      <button
        v-for="item in filters"
        :key="item.key"
        type="button"
        class="chip"
        :class="{ on: filter === item.key }"
        :aria-pressed="filter === item.key"
        @click="filter = item.key"
      >{{ item.label }}</button>
      <button v-if="liveOutputAvailable" type="button" class="live" data-testid="live-output" @click="liveOpen = true">Live output</button>
    </div>

    <section class="card" aria-label="Activity">
      <p v-if="loading" class="hint">Loading…</p>
      <p v-else-if="failed && visible.length === 0" class="hint">Activity could not be loaded.</p>
      <p v-else-if="visible.length === 0" class="hint">{{ filter === 'problems' ? 'No problems.' : 'Nothing here yet.' }}</p>
      <template v-for="item in visible" :key="item.id">
        <div
          class="row"
          :class="{ 'row--run': item.executionId, open: openRun === item.executionId }"
          data-testid="activity-row"
          :role="item.executionId ? 'button' : undefined"
          :tabindex="item.executionId ? 0 : undefined"
          :aria-expanded="item.executionId ? openRun === item.executionId : undefined"
          @click="item.executionId && toggleRun(item.executionId)"
          @keydown.enter.prevent="item.executionId && toggleRun(item.executionId)"
          @keydown.space.prevent="item.executionId && toggleRun(item.executionId)"
        >
          <span class="status" aria-hidden="true">
            <i v-if="item.tone === 'ok'" class="pi pi-check-circle status--ok"></i>
            <i v-else-if="item.tone === 'failed'" class="pi pi-times-circle status--bad"></i>
            <i v-else-if="item.tone === 'running'" class="pi pi-clock status--wait"></i>
          </span>
          <span class="what" :class="{ muted: item.kind === 'change' }">
            <strong v-if="item.subject">{{ item.subject }}</strong>{{ item.subject ? " " : "" }}{{ item.text }}
            <span v-if="item.tone === 'failed' && item.kind === 'run'" class="sr-only"> (failed)</span>
          </span>
          <span class="mono hint">{{ formatActivityTime(item.at) }}</span>
          <i v-if="item.executionId" class="pi pi-chevron-right chevron" :class="{ turned: openRun === item.executionId }" aria-hidden="true"></i>
        </div>
        <div v-if="item.executionId && openRun === item.executionId" class="output-box" data-testid="run-output-box">
          <p v-if="loadingRun === item.executionId" class="hint loading"><ProgressSpinner style="width: 16px; height: 16px" stroke-width="6" /> Loading output…</p>
          <p v-else-if="outputErrors[item.executionId]" class="hint bad">The output could not be loaded. Close this row and open it again to retry.</p>
          <template v-else>
            <pre class="output" data-testid="run-output">{{ outputs[item.executionId] || 'This run printed nothing.' }}</pre>
            <Button label="Copy" icon="pi pi-copy" size="small" severity="secondary" outlined @click="copyOutput(item.executionId)" />
          </template>
        </div>
      </template>
    </section>

    <Dialog v-model:visible="liveOpen" modal header="Live output" :style="{ width: 'min(48rem, 94vw)' }">
      <pre class="output" data-testid="live-output-text">{{ liveLines.length ? liveLines.join('\n') : 'Nothing printed since you opened this page.' }}</pre>
    </Dialog>
  </div>
</template>

<style scoped>
.activity { display: flex; flex-direction: column; gap: 18px; }
.chips { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; }
.chip { min-height: 34px; padding: 0 16px; border: 1px solid var(--line); border-radius: 999px; background: var(--paper); color: var(--ink); font: inherit; font-weight: 500; cursor: pointer; }
.chip.on { border-color: var(--ink); background: var(--ink); color: var(--paper); font-weight: 600; }
.live { margin-left: auto; padding: 0; border: 0; background: none; color: var(--signal-ink); font: inherit; text-decoration: underline; cursor: pointer; }
.card { display: flex; flex-direction: column; padding: 20px; border: 1px solid var(--line); border-radius: var(--radius-card); background: var(--paper); }
.row { display: grid; grid-template-columns: 24px minmax(0, 1fr) auto 16px; align-items: center; gap: 12px; padding: 10px 6px; border-bottom: 1px solid var(--row-line); }
.row--run { cursor: pointer; }
.row--run:hover, .row--run:focus-visible { background: var(--row-line); }
.status { text-align: center; font-size: 16px; }
.status--ok { color: var(--ok); }
.status--bad { color: var(--signal-ink); }
.status--wait { color: var(--ink-soft); }
.what { overflow-wrap: anywhere; }
.chevron { color: var(--ink-soft); font-size: 12px; transition: transform 0.15s; }
.chevron.turned { transform: rotate(90deg); }
.sr-only { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); }
.output-box { display: flex; flex-direction: column; align-items: flex-start; gap: 8px; padding: 10px 6px 14px 42px; border-bottom: 1px solid var(--row-line); }
.loading { display: flex; align-items: center; gap: 8px; }
.bad { color: var(--signal-ink); }
.muted { color: var(--ink-soft); }
.hint { margin: 0; color: var(--ink-soft); font-size: 13px; }
.mono { font-family: var(--mono); font-size: 12px; }
.output { box-sizing: border-box; width: 100%; max-height: 320px; margin: 0; padding: 10px; overflow: auto; border: 1px solid var(--line); border-radius: 6px; white-space: pre-wrap; font-family: var(--mono); font-size: 12px; }
</style>
