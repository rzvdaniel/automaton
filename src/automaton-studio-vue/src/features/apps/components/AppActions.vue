<script setup lang="ts">
import { computed, onUnmounted, ref } from 'vue'
import Button from 'primevue/button'
import ToggleSwitch from 'primevue/toggleswitch'
import { useToast } from 'primevue/usetoast'
import { actionSchedulesService, type ActionModule, type ActionSchedule } from '../services/action-schedules.service'
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
    <section class="card" aria-label="Actions">
      <div v-for="row in rows" :key="row.action.key" class="row" :data-testid="`action-${row.action.key}`">
        <div class="what">
          <strong>{{ row.action.name || row.action.key }}</strong><span v-if="row.action.description" class="hint"> · {{ row.action.description }}</span>
          <div class="hint">
            <span v-if="!row.schedules.length" class="mono">on demand · </span>
            <span>last run </span><span :class="{ bad: row.failed }" data-testid="last-result">{{ row.result }}</span>
            <template v-if="row.run"> · <a href="#" class="logs" @click.prevent="emit('navigate', 'activity')">logs in Activity</a></template>
          </div>
          <div v-for="item in row.schedules" :key="item.schedule.id" class="schedule" data-testid="schedule-row">
            <div class="hint">
              <strong v-if="item.label" data-testid="schedule-name">{{ item.label }} · </strong>
              <span class="mono">{{ item.text }}</span>
              <span v-if="item.nextText"> · {{ item.nextText }}</span>
            </div>
            <ToggleSwitch
              v-if="canUpdate"
              :model-value="item.on"
              :disabled="paused || switching[item.schedule.id]"
              :aria-label="`Schedule ${item.label || item.text} for ${row.action.name || row.action.key}`"
              data-testid="schedule-switch"
              @update:model-value="setEnabled(item.schedule, $event)"
            />
          </div>
        </div>
        <div v-if="canUpdate" class="controls">
          <Button
            :label="row.busy ? 'Running…' : 'Run now'"
            size="small"
            :loading="row.busy"
            :disabled="paused || row.busy"
            data-testid="run-now"
            @click="runNow(row.action.key)"
          />
        </div>
      </div>
    </section>
  </div>
</template>

<style scoped>
.actions { display: flex; flex-direction: column; gap: 18px; }
.card { display: flex; flex-direction: column; padding: 20px; border: 1px solid var(--line); border-radius: var(--radius-card); background: var(--paper); }
.row { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 12px; padding: 12px 0; border-bottom: 1px solid var(--row-line); }
.row:last-of-type { border-bottom: 0; }
.what { min-width: 0; flex: 1 1 280px; overflow-wrap: anywhere; }
.schedule { display: flex; align-items: center; justify-content: space-between; gap: 12px; margin-top: 6px; }
.controls { display: flex; align-items: center; gap: 14px; }
.hint { margin: 0; color: var(--ink-soft); font-size: 13px; }
.mono { font-family: var(--mono); font-size: 12px; }
.bad { color: var(--signal-ink); }
.logs { color: var(--signal-ink); }
</style>
