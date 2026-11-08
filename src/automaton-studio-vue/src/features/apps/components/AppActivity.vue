<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import Dialog from 'primevue/dialog'
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
}>()

const events = ref<ApplicationEvent[]>([])
const secretHistory = ref<SecretAuditEntry[]>([])
const loading = ref(true)
const failed = ref(false)
const filter = ref<ActivityFilter>('all')
const outputOpen = ref(false)
const outputText = ref('')
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

const all = computed(() => buildActivity(props.executions, events.value, secretHistory.value, deployActivity(props.deployTarget ?? null)))
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

async function showOutput(executionId: string) {
  try {
    const [logs, result] = await Promise.all([
      actionSchedulesService.getLogs(props.appId, executionId),
      actionSchedulesService.getResult(props.appId, executionId).catch(() => null),
    ])
    outputText.value = composeExecutionOutput(logs, result)
  } catch {
    outputText.value = 'The output could not be loaded.'
  }
  outputOpen.value = true
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
      <div v-for="item in visible" :key="item.id" class="row" data-testid="activity-row">
        <span :class="{ muted: item.kind === 'change' }">
          <span v-if="item.tone === 'ok'" class="pill pill--ok">ok</span>
          <span v-else-if="item.tone === 'failed'" class="pill pill--bad">failed</span>
          <span v-else-if="item.tone === 'running'" class="pill pill--ok">running</span>
          <strong v-if="item.subject"> {{ item.subject }}</strong> {{ item.text }}
        </span>
        <span class="mono hint">
          {{ formatActivityTime(item.at) }}
          <template v-if="item.executionId"> · <a href="#output" @click.prevent="showOutput(item.executionId)">output</a></template>
        </span>
      </div>
    </section>

    <Dialog v-model:visible="outputOpen" modal header="Output" :style="{ width: 'min(48rem, 94vw)' }">
      <pre class="output" data-testid="run-output">{{ outputText }}</pre>
    </Dialog>
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
.row { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 12px; padding: 10px 0; border-bottom: 1px solid var(--row-line); }
.row:last-child { border-bottom: 0; }
.muted { color: var(--ink-soft); }
.hint { margin: 0; color: var(--ink-soft); font-size: 13px; }
.mono { font-family: var(--mono); font-size: 12px; }
.pill { padding: 3px 10px; border-radius: 999px; font-family: var(--mono); font-size: 12px; }
.pill--ok { border: 1px solid var(--ok); color: var(--ok); }
.pill--bad { background: color-mix(in srgb, var(--signal) 18%, transparent); color: var(--signal-ink); }
.output { max-height: 60vh; margin: 0; overflow: auto; white-space: pre-wrap; font-family: var(--mono); font-size: 12px; }
</style>
