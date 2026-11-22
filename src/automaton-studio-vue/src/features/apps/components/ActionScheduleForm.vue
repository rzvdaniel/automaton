<script setup lang="ts">
import { computed, ref } from 'vue'
import Button from 'primevue/button'
import Checkbox from 'primevue/checkbox'
import InputNumber from 'primevue/inputnumber'
import InputText from 'primevue/inputtext'
import Select from 'primevue/select'
import SelectButton from 'primevue/selectbutton'
import Textarea from 'primevue/textarea'
import { useToast } from 'primevue/usetoast'
import { actionSchedulesService, type ActionModule, type ActionSchedule, type SaveSchedule } from '../services/action-schedules.service'
import { appActionErrorMessage } from '../utils/app-action-error'
import { buildCron, formatNextRuns, nextRuns, timingFromCron, timingFromInterval, timingProblem, type EveryUnit, type Timing, type TimingMode } from '../utils/cron-schedule'

/** Add or edit one schedule of an Action. Cron is the only stored timing: "every" and "daily at" only build it. */
const props = defineProps<{ appId: string; action: ActionModule; schedule: ActionSchedule | null }>()
const emit = defineEmits<{ saved: []; cancel: [] }>()

const toast = useToast()
const busy = ref(false)
const confirmingDelete = ref(false)

function initialTiming(): Timing {
  const s = props.schedule
  if (!s) return timingFromCron('*/15 * * * *')
  if (s.scheduleType === 'Cron') return timingFromCron(s.cronExpression ?? '')
  if (s.scheduleType === 'Interval') return timingFromInterval(s.intervalSeconds ?? 0)
  return timingFromCron('0 2 * * *')
}
const timing = ref<Timing>(initialTiming())
const timeZone = ref(props.schedule?.timeZone || 'UTC')
const skipWhileRunning = ref((props.schedule?.concurrencyPolicy ?? 'SkipIfRunning') !== 'Queue')
const takesInput = computed(() => !!props.action.inputSchema && Object.keys(props.action.inputSchema).length > 0)
const inputText = ref(props.schedule?.inputJson?.trim() ? props.schedule.inputJson : '{}')

const modes: { label: string; value: TimingMode }[] = [{ label: 'every', value: 'every' }, { label: 'daily at', value: 'daily' }, { label: 'cron', value: 'cron' }]
const units: { label: string; value: EveryUnit }[] = [{ label: 'minutes', value: 'minutes' }, { label: 'hours', value: 'hours' }]
const zones = computed(() => {
  const supported = (Intl as unknown as { supportedValuesOf?: (key: string) => string[] }).supportedValuesOf
  const all: string[] = supported ? supported('timeZone') : []
  return ['UTC', ...all.filter((zone) => zone !== 'UTC'), ...(all.includes(timeZone.value) || timeZone.value === 'UTC' ? [] : [timeZone.value])]
})

const problem = computed(() => timingProblem(timing.value))
const cron = computed(() => buildCron(timing.value))
const nextLine = computed(() => {
  if (problem.value) return ''
  const runs = nextRuns(cron.value, timeZone.value)
  return runs.length ? formatNextRuns(runs, timeZone.value) : ''
})
const inputProblem = computed(() => {
  if (!takesInput.value) return null
  try {
    const value = JSON.parse(inputText.value)
    return value && typeof value === 'object' && !Array.isArray(value) ? null : 'The input must be a JSON object.'
  } catch { return 'The input is not valid JSON.' }
})

async function save() {
  if (problem.value || inputProblem.value) return
  const existing = props.schedule
  const body: SaveSchedule = {
    applicationId: props.appId,
    actionKey: props.action.key,
    name: existing?.name || props.action.name || props.action.key,
    description: existing?.description,
    executionScope: existing?.executionScope || props.action.allowedExecutionScopes[0] || 'AppRunner',
    scheduleType: 'Cron',
    cronExpression: cron.value,
    timeZone: timeZone.value,
    enabled: existing?.enabled ?? true,
    concurrencyPolicy: skipWhileRunning.value ? 'SkipIfRunning' : 'Queue',
    misfirePolicy: existing?.misfirePolicy ?? 'Skip',
    input: takesInput.value ? JSON.parse(inputText.value) : {},
  }
  busy.value = true
  try {
    if (existing) await actionSchedulesService.update(existing.id, body)
    else await actionSchedulesService.create(body)
    emit('saved')
  } catch (error) {
    toast.add({ severity: 'error', summary: 'The schedule was not saved', detail: appActionErrorMessage(error, 'Check the timing and try again.'), life: 6000 })
  } finally {
    busy.value = false
  }
}

async function remove() {
  if (!props.schedule) return
  busy.value = true
  try {
    await actionSchedulesService.remove(props.schedule.id)
    emit('saved')
  } catch (error) {
    toast.add({ severity: 'error', summary: 'The schedule was not deleted', detail: appActionErrorMessage(error, 'Try again.'), life: 6000 })
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <form class="form" data-testid="schedule-form" @submit.prevent="save">
    <div class="when">
      <span class="label">Run</span>
      <SelectButton v-model="timing.mode" :options="modes" option-label="label" option-value="value" :allow-empty="false" aria-label="Repeat" data-testid="schedule-mode" />
      <template v-if="timing.mode === 'every'">
        <InputNumber v-model="timing.count" :min="1" :max="timing.unit === 'hours' ? 23 : 59" aria-label="How many" input-class="count" data-testid="schedule-count" />
        <Select v-model="timing.unit" :options="units" option-label="label" option-value="value" aria-label="Unit" data-testid="schedule-unit" />
      </template>
      <input v-else-if="timing.mode === 'daily'" v-model="timing.time" type="time" class="time" aria-label="Time" data-testid="schedule-time" />
      <template v-else>
        <InputText v-model="timing.cron" class="cron mono" aria-label="Cron expression" aria-describedby="cron-help" data-testid="schedule-cron" />
        <span id="cron-help" class="hint wide">minute hour day-of-month month day-of-week, e.g. <code>0 2 * * 1-5</code> = weekdays at 02:00</span>
      </template>
      <Select v-model="timeZone" :options="zones" filter aria-label="Time zone" data-testid="schedule-zone" />
    </div>
    <p v-if="schedule?.scheduleType === 'Interval'" class="hint" data-testid="interval-note">This schedule repeats on a fixed interval. Saving stores it as the timing shown here.</p>
    <p v-if="problem" class="problem" role="alert" data-testid="schedule-problem">{{ problem }}</p>
    <p v-else-if="nextLine" class="hint" data-testid="schedule-next">Next runs: {{ nextLine }}</p>

    <div v-if="takesInput" class="field">
      <label for="schedule-input" class="label">Input</label>
      <Textarea id="schedule-input" v-model="inputText" rows="3" class="mono" data-testid="schedule-input" />
      <p v-if="inputProblem" class="problem" role="alert" data-testid="input-problem">{{ inputProblem }}</p>
    </div>

    <label class="check">
      <Checkbox v-model="skipWhileRunning" binary input-id="skip-while-running" data-testid="schedule-skip" />
      Skip a run while the previous one is still going
    </label>

    <div class="footer">
      <template v-if="schedule && confirmingDelete">
        <span>Delete this schedule?</span>
        <Button label="Delete" size="small" severity="danger" :disabled="busy" data-testid="schedule-delete-confirm" @click="remove" />
        <Button label="Keep it" size="small" severity="secondary" text @click="confirmingDelete = false" />
      </template>
      <Button v-else-if="schedule" label="Delete…" size="small" severity="danger" text :disabled="busy" data-testid="schedule-delete" @click="confirmingDelete = true" />
      <div class="grow" />
      <Button label="Cancel" size="small" severity="secondary" text type="button" @click="emit('cancel')" />
      <Button label="Save" size="small" type="submit" :loading="busy" :disabled="busy || !!problem || !!inputProblem" data-testid="schedule-save" />
    </div>
  </form>
</template>

<style scoped>
.form { display: flex; flex-direction: column; gap: 14px; margin: 2px 0 12px; padding: 16px 18px; border: 1px solid var(--line); border-radius: 10px; background: var(--paper); }
.when { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; }
.label { font-weight: 500; }
.field { display: flex; flex-direction: column; gap: 6px; }
.check { display: flex; align-items: center; gap: 10px; }
.footer { display: flex; align-items: center; gap: 10px; padding-top: 12px; border-top: 1px solid var(--row-line); }
.grow { flex-grow: 1; }
.hint { margin: 0; color: var(--ink-soft); font-size: 13px; }
.wide { flex-basis: 100%; }
.problem { margin: 0; color: var(--signal-ink); font-size: 13px; }
.mono { font-family: var(--mono); }
.cron { flex: 1 1 260px; }
.time { padding: 8px 10px; border: 1px solid var(--line); border-radius: 8px; background: var(--paper); color: var(--ink); font: inherit; }
:deep(.count) { width: 64px; }
</style>
