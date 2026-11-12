<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import Button from 'primevue/button'
import { useToast } from 'primevue/usetoast'
import type { ActionModule, ActionSchedule } from '../services/action-schedules.service'
import { appsService, type AppActionExecution, type CatalogUpdate } from '../services/apps.service'
import type { App, AppDeploymentTarget, AppPermissionStatus, AppSharing } from '../types/apps.types'
import type { AppViewKey } from '../app-navigation'
import {
  appKindOf,
  audienceLabel,
  appOriginLabel,
  describeSchedule,
  secretsPill,
  statusSentence,
  type Segment,
} from '../utils/app-overview'
import AppRestartButton from './AppRestartButton.vue'
import { APP_START_PROGRESS, appActionErrorMessage } from '../utils/app-action-error'
import { getLiveTarget } from '../utils/deployment-target-actions'
import { DeploymentTargetStatus } from '../types/apps.types'

const props = defineProps<{
  app: App
  targets: ReadonlyArray<AppDeploymentTarget>
  executions: ReadonlyArray<AppActionExecution>
  schedules: ReadonlyArray<ActionSchedule>
  actions: ReadonlyArray<ActionModule>
  /** The address people open the app at (the public one once published); empty when it is not live. */
  address: string
  /** The public address, set only while the app is published. */
  publicAddress: string
  /** The sign-in address, for people the app is shared with. */
  privateAddress: string
  sharing: AppSharing | null
}>()
const emit = defineEmits<{ navigate: [view: AppViewKey]; share: []; 'paused-changed': [] }>()
const toast = useToast()

const secretNames = ref<string[] | null>(null)
const update = ref<CatalogUpdate | null>(null)
const updateBusy = ref(false)
const permissions = ref<AppPermissionStatus[]>([])

watch(() => props.app.id, async (id) => {
  secretNames.value = null
  permissions.value = []
  update.value = null
  if (props.app.installedFromRepo && props.app.canUpdate !== false) {
    appsService.getCatalogUpdate(id).then((result) => { if (props.app.id === id && result.available) update.value = result }).catch(() => {})
  }
  const [secrets, perms] = await Promise.allSettled([
    appsService.listSecrets(id),
    appsService.getPermissions(id),
  ])
  if (props.app.id !== id) return
  if (secrets.status === 'fulfilled') secretNames.value = secrets.value.map((secret) => secret.name)
  if (perms.status === 'fulfilled') permissions.value = perms.value.permissions
}, { immediate: true })

/** Only people who can change the app change who opens it. */
const canShare = computed(() => props.app.canUpdate !== false)

/** Replaces the app with the catalog's newer commit and redeploys it; asks first when the owner edited the files. */
async function applyUpdate() {
  const current = update.value
  if (!current || updateBusy.value) return
  if (current.edited && !window.confirm('You edited this app after installing it. Updating replaces your changes with the new version. Update anyway?')) return
  updateBusy.value = true
  try {
    await appsService.applyCatalogUpdate(props.app.id, current.edited)
    update.value = null
    toast.add({ severity: 'success', summary: 'Updating', detail: 'The new version is being deployed.', life: 5000 })
    emit('paused-changed')
  } catch (error) {
    toast.add({ severity: 'error', summary: 'Update failed', detail: appActionErrorMessage(error, 'The app could not be updated.'), life: 8000 })
  } finally {
    updateBusy.value = false
  }
}

const pauseBusy = ref(false)
/** Pauses or resumes the app, then asks the page to reload it. */
async function togglePaused() {
  const pause = !props.app.isPaused
  pauseBusy.value = true
  if (!pause) toast.add({ severity: 'info', summary: 'Resuming', detail: APP_START_PROGRESS, life: 6000 })
  try {
    const state = await appsService.setPaused(props.app.id, pause)
    toast.add({ severity: state.warnings.length > 0 ? 'warn' : 'success', summary: pause ? 'Paused' : 'Resumed', detail: state.warnings[0] ?? (pause ? 'The app is paused.' : 'The app is running again.'), life: 4000 })
    emit('paused-changed')
  } catch (err: unknown) {
    toast.add({ severity: 'error', summary: 'Error', detail: appActionErrorMessage(err, err instanceof Error ? err.message : 'Could not change the app'), life: 12000 })
  } finally {
    pauseBusy.value = false
  }
}
const kind = computed(() => appKindOf(props.app))
const liveTarget = computed(() => getLiveTarget([...props.targets]))
const host = computed(() => (props.address ? new URL(props.address).host : ''))
const lastRun = computed(() => [...props.executions].sort((a, b) => b.createdAtUtc.localeCompare(a.createdAtUtc))[0] ?? null)
const peopleCount = computed(() => props.sharing?.people.length ?? 0)
const sentence = computed<Segment[]>(() => statusSentence({
  kind: kind.value,
  lastRun: lastRun.value,
  schedules: props.schedules,
  host: host.value,
  audience: props.sharing?.audience,
  peopleCount: peopleCount.value,
  lastDeployedAt: liveTarget.value?.lastSuccessfulDeploymentAt,
  deployError: liveTarget.value?.lastDeploymentError,
  failed: liveTarget.value?.status === DeploymentTargetStatus.Failed,
}))

const scheduleFor = (actionKey: string) => {
  const schedule = props.schedules.find((item) => item.actionKey === actionKey && item.enabled)
  return schedule ? describeSchedule(schedule) : 'on demand'
}

const declaredSecrets = computed(() => props.app.application?.secrets ?? [])
const pill = computed(() => secretNames.value === null ? null : secretsPill(declaredSecrets.value, secretNames.value))
const settingNames = computed(() => (props.app.application?.settings ?? []).map((item) => item.name))
/** Settings are saved like secrets, so saved names that are declared settings do not count as secrets. */
const secretCount = computed(() => Math.max(declaredSecrets.value.length, (secretNames.value ?? []).filter((name) => !settingNames.value.includes(name)).length))
const settingCount = computed(() => settingNames.value.length)
const dataDirectory = computed(() => props.app.application?.storage?.dataDirectory ?? '')
const pageUrl = (path: string) => new URL(path, props.address).toString()

async function copy(url: string) {
  try {
    await navigator.clipboard.writeText(url)
    toast.add({ severity: 'success', summary: 'Address copied', life: 2000 })
  } catch {
    toast.add({ severity: 'warn', summary: 'Copy this address', detail: url, life: 6000 })
  }
}
const pages = computed(() => props.app.application?.pages ?? [])
const healthPath = computed(() => props.app.application?.health?.path ?? '')
const whoLabel = computed(() => kind.value === 'api' ? 'Who can call it' : 'Who can open it')
const whoValue = computed(() => kind.value === 'api' && props.sharing?.audience !== 'internet'
  ? 'Callers with an API key'
  : audienceLabel(props.sharing?.audience, peopleCount.value))

const source = computed(() => {
  const runner = liveTarget.value?.runner?.name
  return { origin: appOriginLabel(props.app), runner }
})
</script>

<template>
  <div class="overview">
    <section class="card" aria-label="Status">
      <p class="status" data-testid="status-sentence">
        <template v-for="(part, index) in sentence" :key="index">
          <strong v-if="part.strong">{{ part.text }}</strong>
          <span v-else-if="part.mono" class="mono">{{ part.text }}</span>
          <template v-else>{{ part.text }}</template>
        </template>
      </p>
    </section>

    <section v-if="update" class="card" aria-label="Update available" data-testid="update-card">
      <div class="row">
        <span>
          <span class="pill pill--bad">Update available</span>
          <span class="hint"> · {{ update.installedCommit?.slice(0, 7) }} → {{ update.latestCommit?.slice(0, 7) }}</span>
          <a v-if="update.compareUrl" class="link" :href="update.compareUrl" target="_blank" rel="noopener noreferrer"> What changed</a>
          <template v-if="update.newSettings.length || update.newSecrets.length || update.newPermissions.length">
            <br><span class="hint">New:
              <template v-if="update.newSettings.length">settings {{ update.newSettings.join(', ') }}. </template>
              <template v-if="update.newSecrets.length">secrets {{ update.newSecrets.join(', ') }}. </template>
              <template v-if="update.newPermissions.length">permissions {{ update.newPermissions.join(', ') }} (an administrator approves them again).</template>
            </span>
          </template>
          <br><span v-if="update.edited" class="hint">You edited this app, so updating replaces your changes. </span>
          <span class="hint">Its data, settings, secrets, sharing and schedules are kept.</span>
        </span>
        <Button :label="update.edited ? 'Replace my changes and update' : 'Update'" icon="pi pi-download" size="small" :loading="updateBusy" @click="applyUpdate" />
      </div>
    </section>

    <section v-if="app.isPaused || canShare" class="card" aria-label="Pause" data-testid="pause-card">
      <div class="row">
        <span v-if="app.isPaused"><span class="pill pill--bad">Paused</span> <span class="hint">· stopped, its schedules are off and its address shows a paused page</span></span>
        <span v-else-if="app.actionsOnly" class="hint" data-testid="pause-hint">Changes to settings and secrets apply on the next run. Pause turns its schedules off; its data, secrets, settings and address are kept.</span>
        <span v-else class="hint">Restart loads changed settings and secrets. Pause stops the app and its schedules; its data, secrets, settings and address are kept.</span>
        <AppRestartButton v-if="canShare && !app.isPaused && !app.actionsOnly" :app-id="app.id" />
        <Button
          v-if="canShare"
          :label="app.isPaused ? 'Resume' : 'Pause'"
          :icon="app.isPaused ? 'pi pi-play' : 'pi pi-pause'"
          size="small"
          severity="secondary"
          outlined
          :loading="pauseBusy"
          @click="togglePaused"
        />
      </div>
    </section>

    <section v-if="privateAddress && kind !== 'tool'" class="card" aria-labelledby="addresses-title" data-testid="addresses">
      <h2 id="addresses-title">Addresses</h2>
      <div v-if="publicAddress" class="row">
        <span><strong>Public</strong> <span class="hint">· anyone with the link</span><br><span class="mono">{{ publicAddress }}</span></span>
        <Button label="Copy" icon="pi pi-copy" size="small" severity="secondary" outlined @click="copy(publicAddress)" />
      </div>
      <div class="row">
        <span><strong>Private</strong> <span class="hint">· sign-in, for people it is shared with</span><br><span class="mono">{{ privateAddress }}</span></span>
        <Button label="Copy" icon="pi pi-copy" size="small" severity="secondary" outlined @click="copy(privateAddress)" />
      </div>
    </section>

    <div class="grid">
      <section class="card" aria-labelledby="does-title">
        <div class="card-head">
          <h2 id="does-title">What it does</h2>
          <Button :label="'Activity ›'" size="small" severity="secondary" outlined @click="emit('navigate', 'activity')" />
        </div>
        <p v-if="props.app.application?.description" class="hint" data-testid="app-description">{{ props.app.application.description }}</p>
        <component
          :is="kind === 'web' && address ? 'a' : 'div'"
          v-for="page in pages"
          :key="(page.method ?? '') + page.path"
          class="row"
          :class="{ 'row--link': kind === 'web' && address }"
          v-bind="kind === 'web' && address ? { href: pageUrl(page.path), target: '_blank', rel: 'noopener noreferrer' } : {}"
        >
          <span><span v-if="page.method" class="mono">{{ page.method }} </span><strong>{{ page.label || page.path }}</strong><span v-if="page.description" class="hint"> · {{ page.description }}</span></span>
          <i v-if="kind === 'web' && address" class="pi pi-chevron-right chevron" aria-hidden="true"></i>
        </component>
        <div v-if="pages.length === 0 && kind === 'site'" class="row"><span><strong>Pages</strong> <span class="hint">· files served as they are</span></span></div>
        <div v-else-if="pages.length === 0 && kind === 'web'" class="row"><span><strong>Web pages</strong> <span class="hint">· served at its address</span></span></div>
        <div v-if="kind === 'web' && healthPath" class="row"><span><strong>{{ healthPath }}</strong> <span class="hint">· health check</span></span></div>
        <div v-for="action in actions" :key="action.key" class="row">
          <span><strong>{{ action.name || action.key }}</strong><span v-if="action.description" class="hint"> · {{ action.description }}</span><span v-else-if="kind !== 'tool'" class="hint"> · action</span></span>
          <span class="mono hint">{{ scheduleFor(action.key) }}</span>
        </div>
        <p v-if="kind === 'tool' && actions.length === 0" class="hint">No actions are listed. They show once the app is deployed and its Runner is online.</p>
      </section>

      <section class="card" aria-labelledby="needs-title">
        <div class="card-head">
          <h2 id="needs-title">What it needs</h2>
          <Button label="Settings ›" size="small" severity="secondary" outlined @click="emit('navigate', 'settings')" />
        </div>
        <div v-if="dataDirectory && kind !== 'site' && kind !== 'tool'" class="row"><span>Data folder</span><span class="mono hint">{{ dataDirectory }}</span></div>
        <button v-if="secretCount > 0" type="button" class="row row--link" @click="emit('navigate', 'settings')">
          <span>{{ secretCount }} {{ secretCount === 1 ? 'secret' : 'secrets' }}</span>
          <span class="end">
            <span v-if="pill" class="pill" :class="pill.bad ? 'pill--bad' : 'pill--ok'">{{ pill.text }}</span>
            <i class="pi pi-chevron-right chevron" aria-hidden="true"></i>
          </span>
        </button>
        <button v-if="settingCount > 0" type="button" class="row row--link" data-testid="setting-count" @click="emit('navigate', 'settings')">
          <span>{{ settingCount }} {{ settingCount === 1 ? 'setting' : 'settings' }}</span>
          <i class="pi pi-chevron-right chevron" aria-hidden="true"></i>
        </button>
        <div v-for="item in permissions" :key="item.permission" class="row">
          <span>{{ item.permission }}</span>
          <span class="pill" :class="item.approved ? 'pill--ok' : 'pill--bad'">{{ item.approved ? 'approved' : 'waiting for approval' }}</span>
        </div>
        <p v-if="kind === 'tool' && secretCount === 0 && permissions.length === 0 && settingCount === 0" class="hint">Nothing to set up.</p>
        <div v-if="kind !== 'tool'" class="row">
          <span>{{ whoLabel }}<br><span class="hint">{{ whoValue }}</span></span>
          <Button v-if="canShare" label="Change" size="small" severity="secondary" outlined @click="emit('share')" />
        </div>
      </section>
    </div>

    <p class="source" data-testid="source-line">
      {{ source.origin }}<template v-if="source.runner"> · runs on Runner <span class="mono">{{ source.runner }}</span></template>
    </p>
  </div>
</template>

<style scoped>
.overview { display: flex; flex-direction: column; gap: 18px; }
.card { display: flex; flex-direction: column; gap: 10px; padding: 20px; border: 1px solid var(--line); border-radius: var(--radius-card); background: var(--paper); }
.grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(300px, 1fr)); gap: 16px; }
h2 { margin: 0; font-size: 16px; font-weight: 600; }
.status { margin: 0; font-size: 17px; line-height: 1.5; overflow-wrap: anywhere; }
.row { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 12px; padding: 10px 0; border-bottom: 1px solid var(--row-line); }
.row:last-of-type { border-bottom: 0; }
.hint { color: var(--ink-soft); font-size: 13px; }
p.hint { margin: 0; }
.link { color: var(--signal-ink); text-decoration: underline; }
.mono { font-family: var(--mono); font-size: 12px; }
.status .mono { font-size: 15px; }
.card-head { display: flex; align-items: center; justify-content: space-between; gap: 12px; }
.row--link { width: 100%; border-width: 0 0 1px; background: none; color: inherit; font: inherit; text-align: left; text-decoration: none; cursor: pointer; }
.row--link:hover { background: var(--row-line); }
.chevron { color: var(--ink-soft); font-size: 12px; }
.end { display: inline-flex; align-items: center; gap: 10px; }
.pill { padding: 3px 10px; border-radius: 999px; font-family: var(--mono); font-size: 12px; }
.pill--ok { border: 1px solid var(--ok); color: var(--ok); }
.pill--bad { background: color-mix(in srgb, var(--signal) 18%, transparent); color: var(--signal-ink); }
.source { margin: 0; color: var(--ink-soft); font-size: 13px; }
</style>
