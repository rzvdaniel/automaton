<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import type { ActionModule, ActionSchedule } from '../services/action-schedules.service'
import { appsService, type AppActionExecution } from '../services/apps.service'
import type { App, AppDeploymentTarget, AppPermissionStatus, AppSharing } from '../types/apps.types'
import type { AppViewKey } from '../app-navigation'
import {
  appKindOf,
  audienceLabel,
  describeSchedule,
  statusSentence,
  type Segment,
} from '../utils/app-overview'
import { getLiveTarget } from '../utils/deployment-target-actions'
import { DeploymentTargetStatus } from '../types/apps.types'

const props = defineProps<{
  app: App
  targets: ReadonlyArray<AppDeploymentTarget>
  executions: ReadonlyArray<AppActionExecution>
  schedules: ReadonlyArray<ActionSchedule>
  actions: ReadonlyArray<ActionModule>
  /** The address people open the app at; empty when it is not live. */
  address: string
}>()
const emit = defineEmits<{ navigate: [view: AppViewKey]; share: [] }>()

const secretNames = ref<string[] | null>(null)
const permissions = ref<AppPermissionStatus[]>([])
const sharing = ref<AppSharing | null>(null)

watch(() => props.app.id, async (id) => {
  secretNames.value = null
  permissions.value = []
  sharing.value = null
  const [secrets, perms, share] = await Promise.allSettled([
    appsService.listSecrets(id),
    appsService.getPermissions(id),
    appsService.getSharing(id),
  ])
  if (props.app.id !== id) return
  if (secrets.status === 'fulfilled') secretNames.value = secrets.value.map((secret) => secret.name)
  if (perms.status === 'fulfilled') permissions.value = perms.value.permissions
  if (share.status === 'fulfilled') sharing.value = share.value
}, { immediate: true })

const kind = computed(() => appKindOf(props.app))
const liveTarget = computed(() => getLiveTarget([...props.targets]))
const host = computed(() => (props.address ? new URL(props.address).host : ''))
const lastRun = computed(() => [...props.executions].sort((a, b) => b.createdAtUtc.localeCompare(a.createdAtUtc))[0] ?? null)
const peopleCount = computed(() => sharing.value?.people.length ?? 0)
const sentence = computed<Segment[]>(() => statusSentence({
  kind: kind.value,
  lastRun: lastRun.value,
  schedules: props.schedules,
  host: host.value,
  audience: sharing.value?.audience,
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
const missingSecrets = computed(() => secretNames.value === null
  ? 0
  : declaredSecrets.value.filter((item) => !secretNames.value!.includes(item.name)).length)
const secretCount = computed(() => Math.max(declaredSecrets.value.length, secretNames.value?.length ?? 0))
const dataDirectory = computed(() => props.app.application?.storage?.dataDirectory ?? '')
const pageUrl = (path: string) => new URL(path, props.address).toString()
const pages = computed(() => props.app.application?.pages ?? [])
const healthPath = computed(() => props.app.application?.health?.path ?? '')
const whoLabel = computed(() => kind.value === 'api' ? 'Who can call it' : 'Who can open it')
const whoValue = computed(() => kind.value === 'api' && sharing.value?.audience !== 'internet'
  ? 'Callers with an API key'
  : audienceLabel(sharing.value?.audience, peopleCount.value))

const source = computed(() => {
  const runner = liveTarget.value?.runner?.name
  const origin = props.app.forkedFromAppName
    ? `From the catalog · ${props.app.forkedFromAppName}`
    : props.app.developmentMode === 'local' || props.app.runtimeSource !== 'studio'
      ? 'Deployed with the CLI'
      : 'Built in Studio'
  return { origin, runner }
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

    <div class="grid">
      <section class="card" aria-labelledby="does-title">
        <h2 id="does-title">What it does</h2>
        <div v-for="page in pages" :key="(page.method ?? '') + page.path" class="row">
          <span><span v-if="page.method" class="mono">{{ page.method }} </span><strong>{{ page.path }}</strong><span v-if="page.description" class="hint"> · {{ page.description }}</span></span>
          <a v-if="kind === 'web' && address" :href="pageUrl(page.path)" target="_blank" rel="noopener noreferrer">open</a>
        </div>
        <div v-if="pages.length === 0 && kind === 'site'" class="row"><span><strong>Pages</strong> <span class="hint">· files served as they are</span></span></div>
        <div v-else-if="pages.length === 0 && kind === 'web'" class="row">
          <span><strong>Web pages</strong> <span class="hint">· served at its address</span></span>
          <a v-if="address" :href="address" target="_blank" rel="noopener noreferrer">open</a>
        </div>
        <div v-if="kind === 'web' && healthPath" class="row"><span><strong>{{ healthPath }}</strong> <span class="hint">· health check</span></span></div>
        <div v-for="action in actions" :key="action.key" class="row">
          <span><strong>{{ action.name || action.key }}</strong><span v-if="action.description" class="hint"> · {{ action.description }}</span><span v-else-if="kind !== 'tool'" class="hint"> · action</span></span>
          <span class="mono hint">{{ scheduleFor(action.key) }}</span>
        </div>
        <p v-if="kind === 'tool' && actions.length === 0" class="hint">No actions are listed. They show once the app is deployed and its Runner is online.</p>
        <a href="#activity" class="more" @click.prevent="emit('navigate', 'activity')">{{ kind === 'tool' ? 'See every run' : 'See activity' }}</a>
      </section>

      <section class="card" aria-labelledby="needs-title">
        <h2 id="needs-title">What it needs</h2>
        <div v-if="dataDirectory && kind !== 'site' && kind !== 'tool'" class="row"><span>Data folder</span><span class="mono hint">{{ dataDirectory }}</span></div>
        <div v-if="secretCount > 0" class="row">
          <span>{{ secretCount }} {{ secretCount === 1 ? 'secret' : 'secrets' }}</span>
          <span v-if="secretNames !== null" class="pill" :class="missingSecrets > 0 ? 'pill--bad' : 'pill--ok'">{{ missingSecrets > 0 ? `${missingSecrets} missing` : 'all set' }}</span>
        </div>
        <div v-for="item in permissions" :key="item.permission" class="row">
          <span>{{ item.permission }}</span>
          <span class="pill" :class="item.approved ? 'pill--ok' : 'pill--bad'">{{ item.approved ? 'approved' : 'waiting for approval' }}</span>
        </div>
        <p v-if="kind === 'tool' && secretCount === 0 && permissions.length === 0" class="hint">Nothing to set up.</p>
        <div v-if="kind !== 'tool'" class="row">
          <span>{{ whoLabel }}</span>
          <span class="hint">{{ whoValue }} · <a href="#share" @click.prevent="emit('share')">share</a></span>
        </div>
        <a href="#settings" class="more" @click.prevent="emit('navigate', 'settings')">Change in Settings</a>
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
.mono { font-family: var(--mono); font-size: 12px; }
.status .mono { font-size: 15px; }
.more { font-size: 13px; color: var(--signal-ink); }
.pill { padding: 3px 10px; border-radius: 999px; font-family: var(--mono); font-size: 12px; }
.pill--ok { border: 1px solid var(--ok); color: var(--ok); }
.pill--bad { background: color-mix(in srgb, var(--signal) 18%, transparent); color: var(--signal-ink); }
.source { margin: 0; color: var(--ink-soft); font-size: 13px; }
</style>
