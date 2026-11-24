<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import Button from 'primevue/button'
import { useToast } from 'primevue/usetoast'
import type { ActionModule, ActionSchedule } from '../services/action-schedules.service'
import { appsService, type AppActionExecution, type CatalogUpdate } from '../services/apps.service'
import type { App, AppDeploymentTarget, AppSharing } from '../types/apps.types'
import type { AppViewKey } from '../app-navigation'
import {
  appKindOf,
  appOriginLabel,
  describeSchedule,
  statusSentence,
  type Segment,
} from '../utils/app-overview'
import type { AppDiskUsage } from '../types/apps.types'
import AppDiskCard from './AppDiskCard.vue'
import AppRunsOn from './AppRunsOn.vue'
import PublisherBadge from './PublisherBadge.vue'
import { usePublishers } from '../composables/usePublishers'
import { applyCatalogUpdate, isRetryOnly, shortReason } from '../utils/catalog-update'
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
  sharing: AppSharing | null
  /** Permission requests waiting on this app; only given to system administrators, who can decide them. */
  waitingPermissions?: ReadonlyArray<string>
  deciding?: boolean
}>()
const emit = defineEmits<{ navigate: [view: AppViewKey]; 'paused-changed': []; 'targets-changed': []; 'decide-permission': [permission: string, approved: boolean] }>()
const toast = useToast()
const { publisherOf } = usePublishers()
const publisher = computed(() => publisherOf(props.app.id))

const update = ref<CatalogUpdate | null>(null)
const retryOnly = computed(() => !!update.value && isRetryOnly(update.value))
const showDetails = ref(false)
const updateBusy = ref(false)
const disk = ref<AppDiskUsage | null>(null)

watch(() => props.app.id, async (id) => {
  update.value = null
  disk.value = null
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

const pageUrl = (path: string) => new URL(path, props.address).toString()

const pages = computed(() => props.app.application?.pages ?? [])
const healthPath = computed(() => props.app.application?.health?.path ?? '')
const origin = computed(() => appOriginLabel(props.app))
const dataDirectory = computed(() => props.app.application?.storage?.dataDirectory ?? '')
</script>


<template>
  <div class="overview">
    <section class="line" aria-label="Status">
      <p class="status" data-testid="status-sentence">
        <template v-for="(part, index) in sentence" :key="index">
          <strong v-if="part.strong">{{ part.text }}</strong>
          <span v-else-if="part.mono" class="mono">{{ part.text }}</span>
          <template v-else>{{ part.text }}</template>
        </template>
      </p>
      <p v-if="publisher" class="status__publisher"><PublisherBadge :publisher="publisher" /></p>
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

    <section v-if="update" class="strip" aria-label="Needs you" data-testid="update-card">
      <div class="row">
        <span>
          <span class="pill pill--bad">{{ retryOnly ? 'Last deploy failed' : 'Update available' }}</span>
          <span v-if="update.deployFailed" class="hint" data-testid="update-deploy-error"> · {{ shortReason(update.deployError).replace(/\.$/, '') }}.<template v-if="update.runningCommit"> Still running {{ update.runningCommit.slice(0, 7) }}.</template><template v-else-if="update.hasPreviousVersion"> Still running the previous version.</template></span>
          <span v-if="!retryOnly" class="hint"> · {{ update.installedCommit?.slice(0, 7) }} → {{ update.latestCommit?.slice(0, 7) }}</span>
          <button v-if="update.deployError && update.deployError !== shortReason(update.deployError)" type="button" class="link link--button" data-testid="update-deploy-details-toggle" :aria-expanded="showDetails" @click="showDetails = !showDetails"> {{ showDetails ? 'Hide details' : 'Show details' }}</button>
          <pre v-if="showDetails" class="details" data-testid="update-deploy-details">{{ update.deployError }}</pre>
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
        <Button :label="retryOnly ? 'Retry deploy' : update.edited ? 'Replace my changes and update' : 'Update'" icon="pi pi-download" size="small" :loading="updateBusy" @click="applyUpdate" />
      </div>
    </section>


    <div class="grid">
      <AppRunsOn :app="app" :targets="targets" :origin="origin" @changed="emit('targets-changed')" @navigate="emit('navigate', $event)" />
      <AppDiskCard v-if="disk && typeof disk.totalBytes === 'number'" :disk="disk" :data-directory="kind !== 'site' && kind !== 'tool' ? dataDirectory : ''" />
    </div>

    <div class="wide">
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
        <button v-for="action in actions" :key="action.key" type="button" class="row row--link" :data-testid="`overview-action-${action.key}`" @click="emit('navigate', 'actions')">
          <span><strong>{{ action.name || action.key }}</strong><span v-if="action.description" class="hint"> · {{ action.description }}</span><span v-else-if="kind !== 'tool'" class="hint"> · action</span></span>
          <span class="mono hint">{{ scheduleFor(action.key) }} <i class="pi pi-chevron-right chevron" aria-hidden="true"></i></span>
        </button>
        <p v-if="kind === 'tool' && actions.length === 0" class="hint">No actions are listed. They show once the app is deployed and its Runner is online.</p>
      </section>

    </div>
  </div>
</template>

<style scoped>
.overview { display: flex; flex-direction: column; gap: 18px; }
.card { display: flex; flex-direction: column; gap: 10px; padding: 20px; border: 1px solid var(--line); border-radius: var(--radius-card); background: var(--paper); }
.grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(300px, 1fr)); gap: 16px; align-items: start; }
h2 { margin: 0; font-size: 16px; font-weight: 600; }
.status { margin: 0; font-size: 14px; line-height: 1.5; overflow-wrap: anywhere; }
.row { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 12px; padding: 10px 0; border-bottom: 1px solid var(--row-line); }
.row:last-of-type { border-bottom: 0; }
.hint { color: var(--ink-soft); font-size: 13px; }
p.hint { margin: 0; }
.link { color: var(--signal-ink); text-decoration: underline; }
.mono { font-family: var(--mono); font-size: 12px; }
.status .mono { font-size: 13px; }
.card-head { display: flex; align-items: center; justify-content: space-between; gap: 12px; }
.row--link { width: 100%; border-width: 0 0 1px; background: none; color: inherit; font: inherit; text-align: left; text-decoration: none; cursor: pointer; }
.row--link:hover { background: var(--row-line); }
.chevron { color: var(--ink-soft); font-size: 12px; }
.strip { display: flex; flex-direction: column; gap: 8px; padding: 14px 18px; border-radius: 12px; background: color-mix(in srgb, var(--signal) 14%, var(--paper)); }
.strip-row { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 12px; }
.needs { color: var(--signal-ink); }
.pill { padding: 3px 10px; border-radius: 999px; font-family: var(--mono); font-size: 12px; }
.notice__actions { display: flex; gap: 8px; }
.pill--bad { background: color-mix(in srgb, var(--signal) 18%, transparent); color: var(--signal-ink); }
.link--button { margin-left: 4px; padding: 0; border: 0; background: none; font: inherit; font-size: 13px; cursor: pointer; }
.details { margin: 8px 0 0; padding: 10px 12px; max-height: 280px; overflow: auto; border: 1px solid var(--row-line); border-radius: 10px; font-family: var(--mono); font-size: 12px; white-space: pre-wrap; overflow-wrap: anywhere; }
</style>
