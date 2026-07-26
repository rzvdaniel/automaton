<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue'
import Button from 'primevue/button'
import Card from 'primevue/card'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import Select from 'primevue/select'
import Textarea from 'primevue/textarea'
import ToggleSwitch from 'primevue/toggleswitch'
import { useConfirm } from 'primevue/useconfirm'
import { useToast } from 'primevue/usetoast'
import {
  actionSchedulesService,
  type ActionExecution,
  type ActionModule,
  type ActionSchedule,
  type SaveSchedule,
  type SchedulingCapability,
} from '../services/action-schedules.service'
import { localInputToUtc, utcToLocalInput } from '../utils/schedule-time'
import { isCurrentScheduleLoad } from '../utils/schedule-load'

const props = defineProps<{ appId: string }>()
const confirm = useConfirm()
const toast = useToast()
const actions = ref<ActionModule[]>([])
const schedules = ref<ActionSchedule[]>([])
const executions = ref<ActionExecution[]>([])
const loading = ref(true)
const pending = ref(false)
const error = ref('')
const warning = ref('')
const dialogVisible = ref(false)
const logsVisible = ref(false)
const logsContent = ref('')
const editingId = ref<string>()
const capability = ref<SchedulingCapability>({
  enabled: false,
  maximumSchedules: 0,
  minimumIntervalSeconds: 0,
  maximumConcurrentExecutions: 0,
  monthlyExecutionLimit: 0,
})
let loadGeneration = 0
const inputText = ref('{}')
const form = reactive<SaveSchedule>({
  applicationId: props.appId,
  actionKey: '',
  name: '',
  executionScope: 'AppRunner',
  scheduleType: 'Interval',
  intervalSeconds: 300,
  timeZone: Intl.DateTimeFormat().resolvedOptions().timeZone || 'Etc/UTC',
  enabled: true,
  concurrencyPolicy: 'SkipIfRunning',
  misfirePolicy: 'Skip',
  input: {},
})
const schedulableActions = computed(() => actions.value.filter((action) => action.schedulable && action.allowedExecutionScopes.includes('AppRunner')))
const selectedAction = computed(() => actions.value.find((action) => action.key === form.actionKey))
const actionOptions = computed(() => schedulableActions.value.map((action) => ({
  ...action,
  displayLabel: action.name || action.key,
})))
const effectiveMinimumInterval = computed(() => Math.max(
  capability.value.minimumIntervalSeconds,
  selectedAction.value?.minimumIntervalSeconds ?? 0,
))

function formatDate(value?: string) {
  return value ? new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value)) : '—'
}

async function load(requestedAppId = props.appId) {
  const generation = ++loadGeneration
  loading.value = true
  error.value = ''
  warning.value = ''
  try {
    const [actionResult, allSchedules, allExecutions, currentCapability] = await Promise.all([
      actionSchedulesService.listActions(requestedAppId).then((value) => ({ value, error: '' })).catch(() => ({ value: [] as ActionModule[], error: 'The assigned Runner is unavailable, so deployed Action metadata cannot be refreshed.' })),
      actionSchedulesService.listSchedules(),
      actionSchedulesService.listExecutions(requestedAppId),
      actionSchedulesService.capability(),
    ])
    if (!isCurrentScheduleLoad(requestedAppId, props.appId, generation, loadGeneration)) return
    actions.value = actionResult.value
    warning.value = actionResult.error
    schedules.value = allSchedules.filter((item) => item.applicationId === requestedAppId)
    executions.value = allExecutions
    capability.value = currentCapability
  } catch (reason: any) {
    if (!isCurrentScheduleLoad(requestedAppId, props.appId, generation, loadGeneration)) return
    error.value = reason?.response?.data?.message || reason?.response?.data || 'Scheduling information could not be loaded.'
  } finally {
    if (generation === loadGeneration) loading.value = false
  }
}

function openEditor(schedule?: ActionSchedule) {
  editingId.value = schedule?.id
  const actionKey = schedule?.actionKey || schedulableActions.value[0]?.key || ''
  const actionMinimum = actions.value.find((action) => action.key === actionKey)?.minimumIntervalSeconds ?? 0
  Object.assign(form, {
    applicationId: props.appId,
    actionKey,
    name: schedule?.name || '',
    description: schedule?.description,
    executionScope: 'AppRunner',
    scheduleType: schedule?.scheduleType || 'Interval',
    runAtUtc: utcToLocalInput(schedule?.runAtUtc),
    intervalSeconds: schedule?.intervalSeconds || Math.max(capability.value.minimumIntervalSeconds, actionMinimum),
    cronExpression: schedule?.cronExpression,
    timeZone: schedule?.timeZone || Intl.DateTimeFormat().resolvedOptions().timeZone || 'Etc/UTC',
    enabled: schedule?.enabled ?? true,
    concurrencyPolicy: schedule?.concurrencyPolicy || 'SkipIfRunning',
    misfirePolicy: schedule?.misfirePolicy || 'Skip',
  })
  inputText.value = schedule?.inputJson || '{}'
  dialogVisible.value = true
}

async function save() {
  pending.value = true
  try {
    const input = JSON.parse(inputText.value)
    if (form.scheduleType === 'Interval' && (form.intervalSeconds ?? 0) < effectiveMinimumInterval.value)
      throw new Error(`Interval must be at least ${effectiveMinimumInterval.value} seconds.`)
    const value: SaveSchedule = {
      ...form,
      applicationId: props.appId,
      input,
      runAtUtc: form.scheduleType === 'OneTime' ? localInputToUtc(form.runAtUtc) : undefined,
    }
    editingId.value
      ? await actionSchedulesService.update(editingId.value, value)
      : await actionSchedulesService.create(value)
    dialogVisible.value = false
    toast.add({ severity: 'success', summary: 'Schedule saved', life: 2500 })
    await load()
  } catch (reason: any) {
    toast.add({ severity: 'error', summary: 'Schedule not saved', detail: reason?.response?.data?.message || reason?.response?.data || reason.message, life: 6000 })
  } finally { pending.value = false }
}

async function toggle(schedule: ActionSchedule) {
  pending.value = true
  try {
    await actionSchedulesService.setEnabled(schedule.id, !schedule.enabled)
    await load()
  } catch (reason: any) {
    toast.add({ severity: 'error', summary: 'Schedule not changed', detail: reason?.response?.data?.message || reason?.response?.data, life: 5000 })
  } finally { pending.value = false }
}

function remove(schedule: ActionSchedule) {
  confirm.require({
    message: `Delete “${schedule.name}”?`,
    header: 'Delete schedule',
    icon: 'pi pi-exclamation-triangle',
    rejectProps: { label: 'Cancel', outlined: true },
    acceptProps: { label: 'Delete', severity: 'danger' },
    accept: async () => {
      pending.value = true
      try {
        await actionSchedulesService.remove(schedule.id)
        await load()
      } catch (reason: any) {
        toast.add({ severity: 'error', summary: 'Schedule not deleted', detail: reason?.response?.data?.message || reason?.response?.data || reason.message, life: 5000 })
      } finally { pending.value = false }
    },
  })
}

async function showLogs(execution: ActionExecution) {
  try {
    const result = await actionSchedulesService.getLogs(props.appId, execution.id)
    logsContent.value = result.available ? result.content || 'No log output.' : result.message || 'Logs are unavailable.'
  } catch (reason: any) {
    logsContent.value = reason?.response?.data?.message || 'Logs could not be loaded.'
  }
  logsVisible.value = true
}

onMounted(load)
watch(
  () => props.appId,
  async (newAppId, oldAppId) => {
    if (!newAppId || newAppId === oldAppId) return
    loadGeneration++
    dialogVisible.value = false
    logsVisible.value = false
    editingId.value = undefined
    inputText.value = '{}'
    actions.value = []
    schedules.value = []
    executions.value = []
    form.applicationId = newAppId
    await load(newAppId)
  },
)
</script>

<template>
  <div class="scheduling-panel">
    <div class="panel-header">
      <div><h2>Actions</h2><p>Run and schedule deployed Actions for this app.</p></div>
      <Button label="New schedule" icon="pi pi-calendar-plus" :disabled="pending || !capability.enabled || !schedulableActions.length" @click="openEditor()" />
    </div>
    <div v-if="loading" class="state">Loading Actions and schedules…</div>
    <div v-else-if="error" class="state error">{{ error }} <Button label="Retry" text @click="() => load()" /></div>
    <template v-else>
      <div v-if="warning" class="state warning">{{ warning }}</div>
      <section>
        <h3>Schedulable Actions</h3>
        <div v-if="!schedulableActions.length" class="empty">No deployed Actions currently allow scheduling.</div>
        <div class="card-grid">
          <Card v-for="action in schedulableActions" :key="action.key">
            <template #title>{{ action.name || action.key }}</template>
            <template #subtitle>{{ action.key }}</template>
            <template #content>
              <p>{{ action.description || 'No description provided.' }}</p>
              <small>Effective minimum interval: {{ Math.max(capability.minimumIntervalSeconds, action.minimumIntervalSeconds) }} seconds · Timeout: {{ action.timeoutSeconds }} seconds</small>
            </template>
          </Card>
        </div>
      </section>
      <section>
        <h3>Schedules</h3>
        <div v-if="!schedules.length" class="empty">No schedules yet.</div>
        <div v-for="schedule in schedules" :key="schedule.id" class="row">
          <div><strong>{{ schedule.name }}</strong><span>{{ schedule.actionKey }} · Next: {{ formatDate(schedule.nextRunAtUtc) }}</span></div>
          <span :class="['status', schedule.enabled ? 'enabled' : 'disabled']">{{ schedule.enabled ? 'Enabled' : 'Disabled' }}</span>
          <Button icon="pi pi-pencil" text rounded :disabled="pending" aria-label="Edit schedule" @click="openEditor(schedule)" />
          <Button :icon="schedule.enabled ? 'pi pi-pause' : 'pi pi-play'" text rounded :disabled="pending" :aria-label="schedule.enabled ? 'Disable schedule' : 'Enable schedule'" @click="toggle(schedule)" />
          <Button icon="pi pi-trash" severity="danger" text rounded :disabled="pending" aria-label="Delete schedule" @click="remove(schedule)" />
        </div>
      </section>
      <section>
        <h3>Executions</h3>
        <div v-if="!executions.length" class="empty">No Action executions yet.</div>
        <div v-for="execution in executions" :key="execution.id" class="row execution">
          <div><strong>{{ execution.moduleKey }}</strong><span>Triggered {{ formatDate(execution.createdAtUtc) }} · Duration: {{ execution.startedAtUtc && execution.completedAtUtc ? `${Math.max(0, new Date(execution.completedAtUtc).getTime() - new Date(execution.startedAtUtc).getTime()) / 1000}s` : '—' }}</span><small v-if="execution.failureSummary">{{ execution.failureSummary }}</small></div>
          <span class="status">{{ execution.status }}</span>
          <Button icon="pi pi-file" text rounded aria-label="View execution logs" @click="showLogs(execution)" />
        </div>
      </section>
    </template>
    <Dialog v-model:visible="dialogVisible" modal :header="editingId ? 'Edit schedule' : 'New schedule'" class="schedule-dialog">
      <div class="form-grid">
        <label>Action<Select v-model="form.actionKey" :options="actionOptions" option-label="displayLabel" option-value="key" class="w-full" /></label>
        <label>Name<InputText v-model="form.name" class="w-full" /></label>
        <label>Schedule type<Select v-model="form.scheduleType" :options="['OneTime', 'Interval', 'Cron']" class="w-full" /></label>
        <label v-if="form.scheduleType === 'OneTime'">Run at<input v-model="form.runAtUtc" type="datetime-local" class="native-input" /></label>
        <label v-if="form.scheduleType === 'Interval'">Interval seconds<input v-model.number="form.intervalSeconds" type="number" :min="effectiveMinimumInterval" class="native-input" /></label>
        <label v-if="form.scheduleType === 'Cron'">Cron expression<InputText v-model="form.cronExpression" placeholder="0 * * * *" class="w-full" /></label>
        <small v-if="selectedAction">
          Effective minimum: {{ effectiveMinimumInterval }} seconds (the stricter of your plan and this Action).
          <span v-if="form.scheduleType === 'Cron'">Consecutive cron occurrences must also respect this minimum; the server validates the full schedule.</span>
        </small>
        <label>Time zone<InputText v-model="form.timeZone" class="w-full" /></label>
        <label>Concurrency<Select v-model="form.concurrencyPolicy" :options="['Allow', 'SkipIfRunning', 'Queue']" class="w-full" /></label>
        <label>Misfire policy<Select v-model="form.misfirePolicy" :options="['Skip', 'RunOnceNow']" class="w-full" /></label>
        <label>Action input (JSON)<Textarea v-model="inputText" rows="6" class="w-full" /></label>
        <label class="toggle"><ToggleSwitch v-model="form.enabled" /> Enabled</label>
      </div>
      <template #footer><Button label="Cancel" text :disabled="pending" @click="dialogVisible = false" /><Button label="Save" :loading="pending" :disabled="pending" @click="save" /></template>
    </Dialog>
    <Dialog v-model:visible="logsVisible" modal header="Execution logs" class="schedule-dialog">
      <pre class="logs">{{ logsContent }}</pre>
    </Dialog>
  </div>
</template>

<style scoped>
.scheduling-panel{padding:1.25rem;overflow:auto}.panel-header,.row{display:flex;align-items:center;gap:.75rem}.panel-header{justify-content:space-between}.panel-header h2,.panel-header p{margin:.1rem 0}.panel-header p,.row span,small{color:var(--p-text-muted-color)}section{margin-top:1.5rem}.card-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(240px,1fr));gap:1rem}.row{padding:.8rem;border-bottom:1px solid var(--p-surface-200)}.row>div{display:flex;flex:1;flex-direction:column;gap:.2rem}.status{padding:.25rem .5rem;border-radius:1rem;background:var(--p-surface-100)}.enabled{color:var(--p-green-600)}.disabled{color:var(--p-orange-600)}.empty,.state{padding:1rem;color:var(--p-text-muted-color)}.error{color:var(--p-red-500)}.warning{color:var(--p-orange-600)}.form-grid{display:grid;gap:1rem;min-width:min(32rem,75vw)}label{display:grid;gap:.35rem;font-weight:600}.toggle{display:flex;align-items:center;gap:.5rem}.native-input{padding:.65rem;border:1px solid var(--p-surface-300);border-radius:6px;background:var(--p-surface-0);color:var(--p-text-color)}.logs{max-width:75vw;max-height:65vh;overflow:auto;white-space:pre-wrap}@media(max-width:700px){.panel-header{align-items:flex-start;flex-direction:column}.row{align-items:flex-start;flex-wrap:wrap}.form-grid{min-width:0}}
</style>
