<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import Button from 'primevue/button'
import ToggleSwitch from 'primevue/toggleswitch'
import { useToast } from 'primevue/usetoast'
import { actionSchedulesService, type ActionModule, type ActionSchedule } from '../services/action-schedules.service'
import { actionWebhooksService, type ActionWebhook } from '../services/action-webhooks.service'
import AppActionWebhooks from './AppActionWebhooks.vue'
import ActionScheduleForm from './ActionScheduleForm.vue'
import { appsService, type AppActionExecution } from '../services/apps.service'
import type { AppViewKey } from '../app-navigation'
import { appActionErrorMessage } from '../utils/app-action-error'
import { describeSchedule, formatWhen, isActiveRun, isFailedRun } from '../utils/app-overview'

const props = defineProps<{
  appId: string
  paused: boolean
  /** Only people who may update the app see Run now and the schedule switch. */
  canUpdate: boolean
  actions: ReadonlyArray<ActionModule>
  schedules: ReadonlyArray<ActionSchedule>
  executions: ReadonlyArray<AppActionExecution>
}>()
const emit = defineEmits<{ navigate: [view: AppViewKey]; changed: [] }>()

const toast = useToast()
const running = ref<Record<string, boolean>>({})
const switching = ref<Record<string, boolean>>({})
/** The run Run now started, per action, so its result shows even before the list refreshes. */
const started = ref<Record<string, AppActionExecution>>({})
let stopped = false
onUnmounted(() => { stopped = true })

const webhooks = ref<ActionWebhook[]>([])
async function loadWebhooks() {
  try { webhooks.value = await actionWebhooksService.list(props.appId) } catch { webhooks.value = [] }
}
onMounted(loadWebhooks)
const webhooksFor = (key: string) => webhooks.value.filter((item) => item.actionKey === key)

/** The schedule being edited (its id) or added (`new:<action key>`), and the Action whose webhook is being added. */
const openSchedule = ref<string | null>(null)
const addingWebhook = ref<string | null>(null)
function scheduleSaved() { openSchedule.value = null; emit('changed') }

const schedulesFor = (key: string) => props.schedules.filter((item) => item.actionKey === key)

function lastRunFor(key: string): AppActionExecution | null {
  const runs = [...props.executions, ...(started.value[key] ? [started.value[key]!] : [])]
    .filter((run) => run.moduleKey === key)
    .sort((a, b) => b.createdAtUtc.localeCompare(a.createdAtUtc))
  return runs[0] ?? null
}

function resultText(run: AppActionExecution | null): string {
  if (!run) return 'not run yet'
  const when = formatWhen(run.completedAtUtc ?? run.createdAtUtc)
  if (isActiveRun(run.status)) return 'running…'
  if (run.status === 'Succeeded') return `succeeded ${when}`
  return `${isFailedRun(run.status) ? 'failed' : run.status.toLowerCase()} ${when}`
}

const rows = computed(() => props.actions.map((action) => {
  const run = lastRunFor(action.key)
  const schedules = schedulesFor(action.key).map((schedule) => {
    const on = schedule.enabled && !props.paused
    return {
      schedule,
      on,
      label: schedule.name && schedule.name !== action.key && schedule.name !== action.name ? schedule.name : '',
      text: describeSchedule(schedule),
      nextText: on && schedule.nextRunAtUtc ? `next run ${formatWhen(schedule.nextRunAtUtc)}` : '',
    }
  })
  return {
    action,
    schedules,
    run,
    result: resultText(run),
    failed: !!run && isFailedRun(run.status),
    busy: !!running.value[action.key] || (!!run && isActiveRun(run.status)),
  }
}))

async function runNow(key: string) {
  running.value = { ...running.value, [key]: true }
  try {
    let run = await appsService.runAction(props.appId, key, {})
    started.value = { ...started.value, [key]: run }
    try {
      for (let i = 0; i < 100 && !stopped && isActiveRun(run.status); i++) {
        await new Promise((resolve) => setTimeout(resolve, 1500))
        run = await appsService.getActionExecution(props.appId, run.id)
        started.value = { ...started.value, [key]: run }
      }
    } catch {
      // The run started; the page's own refresh shows how it ended.
    }
    emit('changed')
  } catch (error) {
    toast.add({ severity: 'error', summary: 'It could not be started', detail: appActionErrorMessage(error, 'The Action could not be started.'), life: 6000 })
  } finally {
    running.value = { ...running.value, [key]: false }
  }
}

async function setEnabled(schedule: ActionSchedule, enabled: boolean) {
  const key = schedule.id
  switching.value = { ...switching.value, [key]: true }
  try {
    await actionSchedulesService.setEnabled(schedule.id, enabled)
    emit('changed')
  } catch (error) {
    toast.add({ severity: 'error', summary: 'The schedule was not changed', detail: appActionErrorMessage(error, 'Try again.'), life: 6000 })
  } finally {
    switching.value = { ...switching.value, [key]: false }
  }
}
</script>

<template>
  <div class="actions" data-testid="app-actions">
    <p v-if="paused" class="hint" data-testid="actions-paused-hint">This app is paused, so its schedules are off and Run now is unavailable. Resume it on Overview.</p>
    <p class="intro"><strong>{{ rows.length === 1 ? '1 Action' : `${rows.length} Actions` }}</strong> <span class="hint">Each Action runs one job. Start it by hand, on a schedule or from a webhook; every trigger can be switched off, edited or deleted.</span></p>
    <section v-for="row in rows" :key="row.action.key" class="card" :aria-label="row.action.name || row.action.key" :data-testid="`action-${row.action.key}`">
      <div class="head">
        <div class="what">
          <h2>{{ row.action.name || row.action.key }}</h2>
          <span v-if="row.action.description" class="hint">{{ row.action.description }}</span>
          <span class="last hint">
            <span class="dot" :class="{ bad: row.failed, idle: !row.run }" aria-hidden="true" />
            <span>last run </span><span :class="{ bad: row.failed }" data-testid="last-result">{{ row.result }}</span>
            <template v-if="row.run"> · <a href="#" class="logs" @click.prevent="emit('navigate', 'activity')">runs in Activity</a></template>
          </span>
        </div>
        <Button
          v-if="canUpdate"
          :label="row.busy ? 'Running…' : 'Run now'"
          :loading="row.busy"
          :disabled="paused || row.busy"
          data-testid="run-now"
          @click="runNow(row.action.key)"
        />
      </div>

      <div class="triggers">
        <div class="title">Triggers</div>
        <p v-if="!row.schedules.length && !webhooksFor(row.action.key).length" class="hint none" data-testid="no-triggers">None yet: it runs only when you press Run now.</p>
        <div v-for="item in row.schedules" :key="item.schedule.id" class="trigger" data-testid="schedule-row">
          <div class="line">
            <span class="badge sched">Schedule</span>
            <span class="summary">
              <strong v-if="item.label" data-testid="schedule-name">{{ item.label }} · </strong>
              <span class="mono">{{ item.text }}</span>
              <span v-if="item.nextText" class="hint">· {{ item.nextText }}</span>
            </span>
            <ToggleSwitch
              v-if="canUpdate"
              :model-value="item.on"
              :disabled="paused || switching[item.schedule.id]"
              :aria-label="`Schedule ${item.label || item.text} for ${row.action.name || row.action.key}`"
              data-testid="schedule-switch"
              @update:model-value="setEnabled(item.schedule, $event)"
            />
            <Button v-if="canUpdate" :label="openSchedule === item.schedule.id ? 'Close' : 'Edit'" size="small" severity="secondary" outlined :aria-expanded="openSchedule === item.schedule.id" data-testid="schedule-edit" @click="openSchedule = openSchedule === item.schedule.id ? null : item.schedule.id" />
          </div>
          <ActionScheduleForm v-if="canUpdate && openSchedule === item.schedule.id" :app-id="appId" :action="row.action" :schedule="item.schedule" @saved="scheduleSaved" @cancel="openSchedule = null" />
        </div>
        <ActionScheduleForm v-if="canUpdate && openSchedule === `new:${row.action.key}`" :app-id="appId" :action="row.action" :schedule="null" @saved="scheduleSaved" @cancel="openSchedule = null" />
        <AppActionWebhooks
          :app-id="appId"
          :action-key="row.action.key"
          :action-name="row.action.name || row.action.key"
          :webhooks="webhooksFor(row.action.key)"
          :can-update="canUpdate"
          :adding="addingWebhook === row.action.key"
          @changed="loadWebhooks"
          @stop-adding="addingWebhook = null"
        />
        <div v-if="canUpdate" class="adds">
          <Button v-if="row.action.schedulable" label="+ Add schedule" size="small" severity="secondary" text data-testid="schedule-add" @click="openSchedule = `new:${row.action.key}`" />
          <Button label="+ Add webhook" size="small" severity="secondary" text data-testid="webhook-add" @click="addingWebhook = row.action.key" />
        </div>
      </div>
    </section>
  </div>
</template>

<style scoped>
.actions { display: flex; flex-direction: column; gap: 18px; }
.intro { margin: 0; display: flex; gap: 12px; align-items: baseline; flex-wrap: wrap; }
.card { border: 1px solid var(--line); border-radius: var(--radius-card); background: var(--paper); overflow: hidden; }
.head { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 16px; padding: 16px 20px; }
.what { min-width: 0; flex: 1 1 280px; display: flex; flex-direction: column; gap: 4px; overflow-wrap: anywhere; }
.what h2 { margin: 0; font-size: 16px; font-weight: 600; }
.last { display: flex; align-items: center; gap: 7px; flex-wrap: wrap; }
.dot { width: 8px; height: 8px; border-radius: 999px; background: var(--ok); display: inline-block; }
.dot.bad { background: var(--signal-ink); }
.dot.idle { background: var(--ink-soft); }
.triggers { border-top: 1px solid var(--row-line); padding: 4px 20px 12px; }
.title { font-size: 12px; color: var(--ink-soft); text-transform: uppercase; letter-spacing: .05em; padding: 10px 0 6px; }
.none { padding: 6px 0 10px; }
.trigger { border-top: 1px solid var(--row-line); }
.line { display: flex; align-items: center; gap: 12px; padding: 6px 4px; min-height: 44px; }
.line :deep(.p-toggleswitch) { flex-shrink: 0; }
.summary { flex-grow: 1; display: flex; flex-wrap: wrap; align-items: baseline; gap: 8px; min-width: 0; overflow-wrap: anywhere; }
.badge { font-size: 12px; font-weight: 600; border-radius: 6px; padding: 2px 8px; min-width: 64px; text-align: center; }
.sched { background: var(--panel); color: var(--signal-ink); }
.adds { display: flex; gap: 18px; padding-top: 6px; }
.hint { margin: 0; color: var(--ink-soft); font-size: 13px; }
.mono { font-family: var(--mono); font-size: 13px; }
.bad { color: var(--signal-ink); }
.logs { color: var(--signal-ink); }
</style>
