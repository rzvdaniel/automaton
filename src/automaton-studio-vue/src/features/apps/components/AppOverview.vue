<script setup lang="ts">
import { computed, onUnmounted, reactive, ref, watch } from 'vue'
import Button from 'primevue/button'
import { useToast } from 'primevue/usetoast'
import type { ActionModule, ActionSchedule } from '../services/action-schedules.service'
import { appsService, type AppActionExecution, type CatalogUpdate } from '../services/apps.service'
import { DeploymentTargetStatus, type App, type AppDeploymentTarget, type AppDiskUsage, type AppSharing } from '../types/apps.types'
import type { AppViewKey } from '../app-navigation'
import {
  appKindOf,
  describeSchedule,
  formatLiveAddress,
  formatWhen,
  isActiveRun,
  isFailedRun,
  liveOrigin,
  whoCanOpenLabel,
} from '../utils/app-overview'
import AppDiskCard from './AppDiskCard.vue'
import AppRunsOn from './AppRunsOn.vue'
import { usePublishers } from '../composables/usePublishers'
import { applyCatalogUpdate, isRetryOnly, shortReason } from '../utils/catalog-update'
import { appActionErrorMessage } from '../utils/app-action-error'
import { getLiveTarget } from '../utils/deployment-target-actions'

const props = defineProps<{
  app: App
  targets: ReadonlyArray<AppDeploymentTarget>
  executions: ReadonlyArray<AppActionExecution>
  schedules: ReadonlyArray<ActionSchedule>
  actions: ReadonlyArray<ActionModule>
  /** The address people open the app at (the public one once published); empty when it is not live. */
  address: string
  sharing: AppSharing | null
  /** Permission requests waiting on this app; only given to system administrators, who can decide them. */
  waitingPermissions?: ReadonlyArray<string>
  deciding?: boolean
}>()
const emit = defineEmits<{
  navigate: [view: AppViewKey]
  'paused-changed': []
  'targets-changed': []
  'executions-changed': []
  'decide-permission': [permission: string, approved: boolean]
}>()
const toast = useToast()
const { publisherOf } = usePublishers()
const publisher = computed(() => publisherOf(props.app.id))
const canUpdate = computed(() => props.app.canUpdate !== false)

const update = ref<CatalogUpdate | null>(null)
const retryOnly = computed(() => !!update.value && isRetryOnly(update.value))
const showDetails = ref(false)
const updateBusy = ref(false)
const disk = ref<AppDiskUsage | null>(null)
const addressCopied = ref(false)
const runningKeys = ref<Record<string, boolean>>({})
const startedRuns = ref<Record<string, AppActionExecution>>({})
let stopped = false
onUnmounted(() => { stopped = true })

watch(() => props.app.id, async (id) => {
  update.value = null
  disk.value = null
  startedRuns.value = {}
  if (props.app.canUpdate !== false) {
    appsService.getDisk(id).then((result) => { if (props.app.id === id) disk.value = result }).catch(() => {})
  }
  // Ask even without a recorded source: the server links apps installed before update tracking during this call (#1080).
  if (props.app.canUpdate !== false) {
    appsService.getCatalogUpdate(id).then((result) => { if (props.app.id === id && result.available) update.value = result }).catch(() => {})
  }
}, { immediate: true })

/** Replaces the app with the catalog's newer commit and redeploys it; asks first when the owner edited the files. */
async function applyUpdate() {
  const current = update.value
  if (!current || updateBusy.value) return
  updateBusy.value = true
  try {
    if (await applyCatalogUpdate(props.app.id, current, toast)) {
      update.value = null
      emit('paused-changed')
    }
  } finally {
    updateBusy.value = false
  }
}

const liveTarget = computed(() => getLiveTarget([...props.targets]))
const updateSecrets = reactive<Record<string, string>>({})

/** New secrets the update adds are asked for first, like the install form, then the update runs and deploys. */
async function applyUpdateWithSecrets() {
  const current = update.value
  if (!current || updateBusy.value) return
  const entered = current.newSecrets.filter((name) => updateSecrets[name]?.trim())
  if (entered.length > 0) {
    updateBusy.value = true
    try {
      for (const name of entered) await appsService.setSecret(props.app.id, name, updateSecrets[name]!)
    } catch (error) {
      toast.add({ severity: 'error', summary: 'Secret not saved', detail: appActionErrorMessage(error, 'The secrets could not be saved.'), life: 8000 })
      updateBusy.value = false
      return
    }
    updateBusy.value = false
    for (const name of entered) delete updateSecrets[name]
  }
  await applyUpdate()
}

// An app that was never deployed (installed from the catalog, or through the AI connector) is deployed from here once its required secrets exist.
const neverDeployed = computed(() => props.app.canUpdate !== false && liveTarget.value?.status === DeploymentTargetStatus.NotDeployed)
const savedSecretNames = ref<string[] | null>(null)
const deployBusy = ref(false)
const missingRequiredSecrets = computed(() => {
  const saved = savedSecretNames.value
  if (!saved) return []
  return (props.app.application?.secrets ?? []).filter((item) => !item.optional && !item.from && !saved.includes(item.name)).map((item) => item.name)
})
watch(neverDeployed, async (value) => {
  savedSecretNames.value = null
  if (!value) return
  const id = props.app.id
  const list = await appsService.listSecrets(id).catch(() => null)
  if (props.app.id === id) savedSecretNames.value = (list ?? []).map((item) => item.name)
}, { immediate: true })

async function deployNow() {
  if (deployBusy.value || missingRequiredSecrets.value.length > 0) return
  deployBusy.value = true
  try {
    const operation = await appsService.deployStoredSource(props.app.id)
    toast.add({ severity: 'success', summary: 'Deploying', detail: 'The app is being deployed.', life: 5000 })
    emit('targets-changed')
    for (let attempt = 0; attempt < 150; attempt++) {
      const current = await appsService.getDeploymentOperation(props.app.id, operation.operationId)
      if (current.status === 'Succeeded') break
      if (current.status === 'Failed') throw new Error(current.error ?? 'The deployment failed.')
      await new Promise((resolve) => setTimeout(resolve, 2000))
    }
  } catch (error) {
    toast.add({ severity: 'error', summary: 'Deploy failed', detail: appActionErrorMessage(error, 'The app could not be deployed.'), life: 8000 })
  } finally {
    deployBusy.value = false
    emit('targets-changed')
  }
}

const kind = computed(() => appKindOf(props.app))
const displayAddress = computed(() => formatLiveAddress(props.address))
const whoLabel = computed(() => whoCanOpenLabel(kind.value, props.sharing?.audience, props.sharing?.people.length ?? 0, props.sharing?.team?.name))
const originInfo = computed(() => liveOrigin(props.app))
const dataDirectory = computed(() => props.app.application?.storage?.dataDirectory ?? '')

async function copyAddress() {
  if (!props.address) return
  try {
    await navigator.clipboard.writeText(props.address)
    addressCopied.value = true
    toast.add({ severity: 'success', summary: 'Address copied', life: 2000 })
    setTimeout(() => { addressCopied.value = false }, 1500)
  } catch {
    toast.add({ severity: 'warn', summary: 'Copy this address', detail: props.address, life: 6000 })
  }
}

function scheduleTitle(schedule: ActionSchedule): string {
  const prefix = `${props.app.name}: `
  const raw = schedule.name?.startsWith(prefix) ? schedule.name.slice(prefix.length) : schedule.name
  if (raw && raw !== schedule.actionKey) return raw
  const action = props.actions.find((item) => item.key === schedule.actionKey)
  return action?.name || raw || schedule.actionKey
}

function lastRunFor(actionKey: string): AppActionExecution | null {
  const local = startedRuns.value[actionKey]
  const runs = [...props.executions, ...(local ? [local] : [])]
    .filter((run) => run.moduleKey === actionKey)
    .sort((a, b) => b.createdAtUtc.localeCompare(a.createdAtUtc))
  if (local && runs[0] && runs[0].createdAtUtc === local.createdAtUtc) return local
  return runs[0] ?? null
}

type RunTone = 'ok' | 'bad' | 'busy' | 'none'

function runSummary(run: AppActionExecution | null): { tone: RunTone; text: string } {
  if (!run) return { tone: 'none', text: 'No runs yet' }
  if (isActiveRun(run.status)) return { tone: 'busy', text: 'Running now' }
  const when = formatWhen(run.completedAtUtc ?? run.createdAtUtc)
  if (run.status === 'Succeeded') return { tone: 'ok', text: `Last run ${when}, ok` }
  if (isFailedRun(run.status)) {
    const reason = shortReason(run.failureSummary)
    return { tone: 'bad', text: reason ? `Last run failed: ${reason}` : `Last run failed ${when}` }
  }
  return { tone: 'none', text: `Last run ${run.status.toLowerCase()} ${when}` }
}

const scheduleRows = computed(() => props.schedules.map((schedule) => {
  const run = lastRunFor(schedule.actionKey)
  const summary = runSummary(run)
  const nextText = props.app.isPaused
    ? 'paused'
    : !schedule.enabled
      ? 'off'
      : schedule.nextRunAtUtc
        ? `next ${formatWhen(schedule.nextRunAtUtc)}`
        : ''
  return {
    schedule,
    title: scheduleTitle(schedule),
    cadence: describeSchedule(schedule),
    runTone: summary.tone,
    lastText: summary.text,
    nextText,
    busy: !!runningKeys.value[schedule.actionKey] || (!!run && isActiveRun(run.status)),
  }
}))

function scheduleInput(schedule: ActionSchedule): Record<string, unknown> {
  if (!schedule.inputJson?.trim()) return {}
  try {
    const parsed = JSON.parse(schedule.inputJson)
    return parsed && typeof parsed === 'object' && !Array.isArray(parsed) ? (parsed as Record<string, unknown>) : {}
  } catch {
    return {}
  }
}

async function runScheduleNow(schedule: ActionSchedule) {
  const key = schedule.actionKey
  runningKeys.value = { ...runningKeys.value, [key]: true }
  try {
    let run = await appsService.runAction(props.app.id, key, scheduleInput(schedule))
    startedRuns.value = { ...startedRuns.value, [key]: run }
    emit('executions-changed')
    try {
      for (let i = 0; i < 100 && !stopped && isActiveRun(run.status); i++) {
        await new Promise((resolve) => setTimeout(resolve, 1500))
        run = await appsService.getActionExecution(props.app.id, run.id)
        startedRuns.value = { ...startedRuns.value, [key]: run }
      }
    } catch {
      // The run started; parent polling still updates when it finishes.
    }
    emit('executions-changed')
  } catch (error) {
    toast.add({ severity: 'error', summary: 'It could not be started', detail: appActionErrorMessage(error, 'The Action could not be started.'), life: 6000 })
  } finally {
    runningKeys.value = { ...runningKeys.value, [key]: false }
  }
}
</script>

<template>
  <div class="overview">
    <section v-if="update" class="strip strip--update" aria-label="Needs you" data-testid="update-card">
      <div class="update-copy">
        <div class="update-title">
          <strong class="needs">{{ retryOnly ? 'Last deploy failed' : 'Update available' }}</strong>
          <template v-if="!retryOnly && (update.installedCommit || update.latestCommit)">
            <code v-if="update.installedCommit" class="commit-chip">{{ update.installedCommit.slice(0, 7) }}</code>
            <template v-if="update.installedCommit && update.latestCommit"> → </template>
            <code v-if="update.latestCommit" class="commit-chip">{{ update.latestCommit.slice(0, 7) }}</code>
          </template>
        </div>
        <div class="update-body">
          <span v-if="update.deployFailed" data-testid="update-deploy-error">
            {{ shortReason(update.deployError).replace(/\.$/, '') }}.<template v-if="update.runningCommit"> Still running {{ update.runningCommit.slice(0, 7) }}.</template><template v-else-if="update.hasPreviousVersion"> Still running the previous version.</template>
          </span>
          <button v-if="update.deployError && update.deployError !== shortReason(update.deployError)" type="button" class="link link--button" data-testid="update-deploy-details-toggle" :aria-expanded="showDetails" @click="showDetails = !showDetails">{{ showDetails ? 'Hide details' : 'Show details' }}</button>
          <pre v-if="showDetails" class="details" data-testid="update-deploy-details">{{ update.deployError }}</pre>
          <template v-if="update.newSettings.length"> New settings: {{ update.newSettings.join(', ') }}.</template>
          <template v-if="update.newSecrets.length"> New secrets: {{ update.newSecrets.join(', ') }}.</template>
          <template v-if="update.newPermissions.length"> New permissions: {{ update.newPermissions.join(', ') }} (an administrator approves them again).</template>
          <template v-if="update.edited"> You edited this app, so updating replaces your changes.</template>
          Data, settings, secrets, sharing and schedules are kept.
          <a v-if="update.compareUrl" class="link" :href="update.compareUrl" target="_blank" rel="noopener noreferrer">What changed</a>
        </div>
        <div v-if="update.newSecrets.length" class="newsecrets" data-testid="update-secrets">
          <label v-for="name in update.newSecrets" :key="name" class="newsecret"><span class="mono">{{ name }}</span>
            <input v-model="updateSecrets[name]" type="password" autocomplete="off" :aria-label="`Value for ${name}`" :data-testid="`update-secret-${name}`">
          </label>
        </div>
      </div>
      <Button :label="retryOnly ? 'Retry deploy' : update.edited ? 'Replace my changes and update' : 'Update'" :loading="updateBusy" class="update-btn" @click="applyUpdateWithSecrets" />
    </section>

    <section v-if="app.isPaused" class="strip" aria-label="Paused" data-testid="pause-card">
      <span><strong class="needs">Paused</strong> <span class="hint">· stopped, its schedules are off and its address shows a paused page. Resume it from the ⋯ menu.</span>
        <span v-if="app.pausedReason" class="hint" data-testid="paused-reason"><br>Reason: {{ app.pausedReason }}</span></span>
    </section>

    <section v-if="waitingPermissions?.length" class="strip" aria-label="Permission requests" data-testid="permission-notice">
      <div v-for="permission in waitingPermissions" :key="permission" class="strip-row">
        <span><strong class="needs">Waiting for your approval</strong> <span class="mono">{{ permission }}</span> <span class="hint">· this app asks for it</span></span>
        <span class="notice__actions">
          <Button label="Approve" size="small" :aria-label="`Approve ${permission}`" :disabled="deciding" @click="emit('decide-permission', permission, true)" />
          <Button label="Deny" size="small" severity="secondary" outlined :aria-label="`Deny ${permission}`" :disabled="deciding" @click="emit('decide-permission', permission, false)" />
        </span>
      </div>
    </section>

    <section v-if="neverDeployed && !update" class="strip" aria-label="Deploy" data-testid="deploy-card">
      <div class="strip-row">
        <span>
          <strong class="needs">Not deployed yet</strong>
          <span v-if="missingRequiredSecrets.length" class="hint" data-testid="deploy-blocked"> · waiting for the required secret<template v-if="missingRequiredSecrets.length > 1">s</template> <span class="mono">{{ missingRequiredSecrets.join(', ') }}</span>. Save <template v-if="missingRequiredSecrets.length > 1">them</template><template v-else>it</template> under Secrets, then deploy.</span>
          <span v-else class="hint"> · deploy it to start it.</span>
        </span>
        <Button label="Deploy" icon="pi pi-play" size="small" data-testid="deploy-button" :loading="deployBusy" :disabled="missingRequiredSecrets.length > 0 || savedSecretNames === null" @click="deployNow" />
      </div>
    </section>

    <section v-if="liveTarget?.status === DeploymentTargetStatus.Failed && !update" class="strip" aria-label="Deploy failed" data-testid="deploy-failed-card">
      <span>
        <strong class="needs">Last deploy failed</strong>
        <span v-if="liveTarget.lastDeploymentError" class="hint"> · {{ shortReason(liveTarget.lastDeploymentError) }}</span>
        <span v-if="liveTarget.lastSuccessfulDeploymentAt" class="hint"> Still running the previous version.</span>
      </span>
    </section>

    <div class="cards">
      <section class="card" :class="{ 'card--wide': scheduleRows.length === 0 }" aria-labelledby="live-title" data-testid="live-card">
        <h2 id="live-title">Live</h2>
        <div class="kv-rows">
          <template v-if="kind !== 'tool'">
            <div class="kv-row">
              <span class="kv-label">Address</span>
              <span class="kv-value kv-address">
                <template v-if="address">
                  <span class="address-text" data-testid="live-address" :title="address">{{ displayAddress }}</span>
                  <button type="button" class="icon-copy" :aria-label="addressCopied ? 'Copied address' : 'Copy address'" data-testid="copy-live-address" @click="copyAddress">
                    <i :class="addressCopied ? 'pi pi-check' : 'pi pi-copy'" aria-hidden="true" />
                  </button>
                </template>
                <span v-else class="hint" data-testid="live-address">Not live yet</span>
              </span>
            </div>
            <div class="kv-row">
              <span class="kv-label">Who</span>
              <span class="kv-value" data-testid="live-who">{{ whoLabel }}</span>
            </div>
          </template>
          <div class="kv-row" data-testid="installed-from">
            <span class="kv-label">{{ originInfo.label }}</span>
            <span class="kv-value wrap">{{ originInfo.value }}</span>
          </div>
          <div v-if="publisher" class="kv-row" data-testid="live-publisher">
            <span class="kv-label">Publisher</span>
            <span class="kv-value pub">
              <img v-if="publisher.logo" class="pub__logo" :src="publisher.logo" alt="" width="14" height="14" loading="lazy" referrerpolicy="no-referrer" />
              <span>{{ publisher.name }}</span>
              <i v-if="publisher.verified" class="pi pi-verified pub__tick" role="img" aria-label="Verified publisher" title="Verified: published by Automaton in the official catalog" />
            </span>
          </div>
        </div>
      </section>

      <section v-if="scheduleRows.length > 0" class="card" aria-labelledby="scheduled-title" data-testid="scheduled-card">
        <h2 id="scheduled-title">Scheduled</h2>
        <div class="sched-list">
          <div v-for="item in scheduleRows" :key="item.schedule.id" class="sched-row" data-testid="scheduled-row">
            <div class="sched-main">
              <div class="sched-title">
                <strong>{{ item.title }}</strong> <span class="sched-cadence">· {{ item.cadence }}</span>
              </div>
              <div class="sched-meta">
                <span class="sched-run" :class="`sched-run--${item.runTone}`" data-testid="scheduled-last-run">
                  <span v-if="item.runTone !== 'none'" aria-hidden="true">● </span>{{ item.lastText }}
                </span>
                <template v-if="item.nextText"> · {{ item.nextText }}</template>
              </div>
            </div>
            <Button
              v-if="canUpdate"
              :label="item.busy ? 'Running…' : 'Run now'"
              size="small"
              severity="secondary"
              outlined
              :loading="item.busy"
              :disabled="app.isPaused || item.busy"
              data-testid="schedule-run-now"
              @click="runScheduleNow(item.schedule)"
            />
          </div>
        </div>
      </section>
    </div>

    <div class="cards">
      <AppRunsOn :app="app" :targets="targets" @changed="emit('targets-changed')" @navigate="emit('navigate', $event)" />
      <AppDiskCard v-if="disk && typeof disk.totalBytes === 'number'" :disk="disk" :data-directory="kind !== 'site' && kind !== 'tool' ? dataDirectory : ''" />
    </div>
  </div>
</template>

<style scoped>
.overview { display: flex; flex-direction: column; gap: 20px; }
.cards { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 20px; align-items: stretch; }
.cards > :only-child, .card--wide { grid-column: 1 / -1; }
.card { display: flex; flex-direction: column; gap: 4px; height: 100%; padding: 22px 24px; border: 1px solid var(--line); border-radius: var(--radius-card); background: var(--paper); }
h2 { margin: 0 0 10px; font-size: 17px; font-weight: 600; }

.strip { display: flex; flex-direction: column; gap: 8px; padding: 14px 18px; border-radius: 12px; background: color-mix(in srgb, var(--signal) 14%, var(--paper)); border: 1px solid color-mix(in srgb, var(--signal) 32%, var(--paper)); }
.strip--update { flex-direction: row; flex-wrap: wrap; align-items: center; gap: 16px; padding: 18px 22px; }
.update-copy { flex: 1 1 320px; min-width: 0; display: flex; flex-direction: column; gap: 4px; font-size: 14px; }
.update-title { display: flex; flex-wrap: wrap; align-items: center; gap: 6px; font-size: 15px; }
.update-body { color: var(--ink); line-height: 1.45; }
.commit-chip { font-family: var(--mono); font-size: 12px; background: var(--paper); color: var(--ink); padding: 2px 6px; border-radius: 4px; border: 1px solid color-mix(in srgb, var(--signal) 22%, var(--line)); }
.update-btn { flex-shrink: 0; }
.strip-row { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 12px; }
.needs { color: var(--signal-ink); }
.notice__actions { display: flex; gap: 8px; }
.newsecrets { display: flex; flex-direction: column; gap: 6px; margin-top: 8px; }
.newsecret { display: flex; align-items: center; gap: 8px; }
.newsecret input { flex: 1; min-width: 0; padding: 6px 8px; border: 1px solid var(--line); border-radius: 6px; background: var(--paper); color: inherit; }

.kv-rows { display: flex; flex-direction: column; }
.kv-row { display: grid; grid-template-columns: 110px 1fr; align-items: center; gap: 0 12px; font-size: 14px; border-bottom: 1px solid var(--row-line); }
.kv-row:last-child { border-bottom: 0; }
.kv-label { color: var(--ink-soft); padding: 11px 0; }
.kv-value { padding: 11px 0; min-width: 0; }
.wrap { overflow-wrap: anywhere; }
.kv-address { display: flex; align-items: center; gap: 6px; min-width: 0; }
.address-text { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.icon-copy { width: 24px; height: 24px; flex-shrink: 0; display: inline-flex; align-items: center; justify-content: center; border: 0; border-radius: 6px; background: transparent; color: var(--ink-soft); cursor: pointer; padding: 0; }
.icon-copy:hover { color: var(--ink); background: var(--row-line); }
.pub { display: inline-flex; align-items: center; gap: 6px; }
.pub__logo { width: 14px; height: 14px; border-radius: 3px; object-fit: contain; }
.pub__tick { font-size: 13px; color: var(--ink-soft); }

.sched-list { display: flex; flex-direction: column; }
.sched-row { display: flex; align-items: center; gap: 14px; padding: 11px 0; border-top: 1px solid var(--row-line); }
.sched-row:first-child { border-top: 0; }
.sched-main { flex: 1; display: flex; flex-direction: column; gap: 3px; min-width: 0; }
.sched-title { font-size: 15px; overflow-wrap: anywhere; }
.sched-cadence { font-weight: 400; color: var(--ink-soft); }
.sched-meta { font-size: 13px; color: var(--ink-soft); overflow-wrap: anywhere; }
.sched-run--ok { color: #166534; }
.sched-run--bad { color: #b91c1c; }
.sched-run--busy { color: #92400e; }
:global(.app-dark) .sched-run--ok { color: #86efac; }
:global(.app-dark) .sched-run--bad { color: #fca5a5; }
:global(.app-dark) .sched-run--busy { color: #fcd34d; }

.card :deep(.p-button-outlined.p-button-secondary) {
  background: var(--paper);
  border-color: var(--line);
  color: var(--ink);
  white-space: nowrap;
}
.card :deep(.p-button-outlined.p-button-secondary:hover) {
  background: var(--panel);
  border-color: var(--line);
  color: var(--ink);
}

.hint { color: var(--ink-soft); font-size: 13px; }
.link { color: var(--signal-ink); text-decoration: underline; }
.link--button { margin-left: 4px; padding: 0; border: 0; background: none; font: inherit; font-size: 13px; cursor: pointer; }
.mono { font-family: var(--mono); font-size: 12px; }
.details { margin: 8px 0 0; padding: 10px 12px; max-height: 280px; overflow: auto; border: 1px solid var(--row-line); border-radius: 10px; font-family: var(--mono); font-size: 12px; white-space: pre-wrap; overflow-wrap: anywhere; }

@media (max-width: 720px) {
  .cards { grid-template-columns: 1fr; }
  .card--wide { grid-column: auto; }
}
</style>
