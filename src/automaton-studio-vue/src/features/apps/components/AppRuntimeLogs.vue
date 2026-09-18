<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import Button from 'primevue/button'
import Card from 'primevue/card'
import Message from 'primevue/message'
import ProgressSpinner from 'primevue/progressspinner'
import Select from 'primevue/select'
import Tag from 'primevue/tag'
import { applicationEventsService } from '@/shared/services/api/application-events.service'
import type { ApplicationEvent, ApplicationEventFilter } from '@/shared/types/ai.types'
import type { AppDeploymentTarget } from '../types/apps.types'
import type { ConsoleEntry, RuntimeLogConnectionState } from '../types/console.types'
import type { AppViewKey } from '../app-navigation'
import { getDeploymentTargetStatusLabel, hasContradictoryDeploymentTargetState } from '../utils/app-operational-summary'
import { getEnvironmentDisplayName } from '../utils/environment-display'

type Severity = 'secondary' | 'info' | 'success' | 'warn' | 'danger'

interface RuntimeStatus {
  label: string
  severity: Severity
  description: string
  unavailable: boolean
  noticeSeverity: 'info' | 'warn' | 'error'
  actions: AppViewKey[]
}

const props = defineProps<{
  appId: string
  appName: string
  targets: ReadonlyArray<AppDeploymentTarget>
  selectedTarget: AppDeploymentTarget | null
  liveLogs: ReadonlyArray<ConsoleEntry>
  connectionState: RuntimeLogConnectionState
  isStreamSubscribed: boolean
  streamError: boolean
}>()

const emit = defineEmits<{
  selectTarget: [target: AppDeploymentTarget]
  navigate: [view: AppViewKey]
}>()

const activity = ref<ApplicationEvent[]>([])
const totalCount = ref(0)
const totalPages = ref(0)
const isLoadingActivity = ref(false)
const hasLoadedActivity = ref(false)
const activityError = ref(false)
const expandedActivityId = ref<string | null>(null)
const filter = ref<ApplicationEventFilter>({ page: 1, pageSize: 50 })

const targetOptions = computed(() => props.targets.map((target) => ({
  label: `${getEnvironmentDisplayName(target)} · ${getDeploymentTargetStatusLabel(target.status)}`,
  value: target.slug,
  target,
})))

const visibleRuntimeLogs = computed(() => props.liveLogs.filter((entry) =>
  entry.type === 'runtime-log' && entry.deploymentTargetId === props.selectedTarget?.id,
))

const runtimeStatus = computed<RuntimeStatus>(() => {
  const target = props.selectedTarget
  if (!target) {
    return {
      label: 'No environment',
      severity: 'secondary',
      description: 'No deployment environment is available for this app.',
      unavailable: true,
      noticeSeverity: 'info',
      actions: ['deployments'],
    }
  }

  if (hasContradictoryDeploymentTargetState(target)) {
    const assignment = target.runner ? 'a runtime is assigned' : 'no runtime is assigned'
    const runtime = target.genericRuntimeDeployed
      ? 'an application-runtime record is present'
      : 'no application-runtime record is present'
    return {
      label: 'Needs review',
      severity: 'warn',
      description: `The target reports ${target.status}, while ${assignment} and ${runtime}. Live logs are unavailable until these facts agree.`,
      unavailable: true,
      noticeSeverity: 'warn',
      actions: ['runtime', 'deployments'],
    }
  }

  const hasAssignment = !!target.runner && target.status !== 'Unassigned'
  if (!hasAssignment) {
    return {
      label: 'No runtime assigned',
      severity: 'secondary',
      description: 'No runtime is assigned to this environment.',
      unavailable: true,
      noticeSeverity: 'info',
      actions: ['deployments'],
    }
  }

  if (target.status === 'RunnerOffline' || !target.runner?.isOnline) {
    return {
      label: 'Runtime offline',
      severity: 'warn',
      description: 'The assigned runtime is offline, so live runtime logs are unavailable.',
      unavailable: true,
      noticeSeverity: 'warn',
      actions: ['runtime', 'deployments'],
    }
  }

  if (!target.genericRuntimeDeployed) {
    return {
      label: 'No app runtime deployed',
      severity: 'secondary',
      description: 'This environment does not currently have a deployed application runtime.',
      unavailable: true,
      noticeSeverity: 'info',
      actions: ['runtime', 'deployments'],
    }
  }

  if (props.streamError) {
    return {
      label: 'Connection issue',
      severity: 'danger',
      description: 'Studio could not connect to live runtime logs for this environment. Check Runtime or Deployments for current status.',
      unavailable: true,
      noticeSeverity: 'error',
      actions: ['runtime', 'deployments'],
    }
  }

  if (props.connectionState === 'reconnecting') {
    return {
      label: 'Reconnecting',
      severity: 'warn',
      description: 'The live log connection is reconnecting. Any entries shown below are buffered in this app session.',
      unavailable: false,
      noticeSeverity: 'warn',
      actions: ['runtime'],
    }
  }

  if (props.connectionState === 'connecting' || !props.isStreamSubscribed) {
    return {
      label: 'Connecting',
      severity: 'info',
      description: `Connecting to live runtime logs for ${getEnvironmentDisplayName(target)}.`,
      unavailable: false,
      noticeSeverity: 'info',
      actions: ['runtime'],
    }
  }

  if (props.connectionState !== 'connected') {
    return {
      label: 'Disconnected',
      severity: 'warn',
      description: 'The live log connection is disconnected. Any entries shown below are buffered in this app session.',
      unavailable: true,
      noticeSeverity: 'warn',
      actions: ['runtime', 'deployments'],
    }
  }

  return {
    label: 'Connected',
    severity: 'success',
    description: `Live runtime log stream for ${getEnvironmentDisplayName(target)}.`,
    unavailable: false,
    noticeSeverity: 'info',
    actions: ['runtime', 'deployments'],
  }
})

async function loadActivity() {
  if (!props.appId) return
  isLoadingActivity.value = true
  activityError.value = false
  try {
    const result = await applicationEventsService.getEvents(props.appId, filter.value)
    activity.value = result.items
    totalCount.value = result.totalCount
    totalPages.value = result.totalPages
  } catch {
    activityError.value = true
  } finally {
    isLoadingActivity.value = false
    hasLoadedActivity.value = true
  }
}

function selectTarget(slug: string | null | undefined) {
  const selected = props.targets.find((target) => target.slug === slug)
  if (selected) emit('selectTarget', selected)
}

function navigate(view: AppViewKey) {
  emit('navigate', view)
}

function getNavigationLabel(view: AppViewKey) {
  return view === 'runtime' ? 'View runtime' : 'View deployments'
}

function prevPage() {
  if ((filter.value.page ?? 1) <= 1) return
  filter.value.page = (filter.value.page ?? 1) - 1
  void loadActivity()
}

function nextPage() {
  if ((filter.value.page ?? 1) >= totalPages.value) return
  filter.value.page = (filter.value.page ?? 1) + 1
  void loadActivity()
}

function toggleActivity(id: string) {
  expandedActivityId.value = expandedActivityId.value === id ? null : id
}

function formatDate(dateStr: string) {
  const date = new Date(dateStr)
  return Number.isNaN(date.getTime()) ? 'Date unavailable' : date.toLocaleString()
}

function formatLogTime(date: Date) {
  if (Number.isNaN(date.getTime())) return 'Time unavailable'
  const time = date.toLocaleTimeString(undefined, {
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
    })
  return `${time}.${String(date.getMilliseconds()).padStart(3, '0')}`
}

function formatCategory(category: string) {
  return category ? category.charAt(0).toUpperCase() + category.slice(1) : 'Activity'
}

function formatLevel(level: string) {
  switch (level.toLowerCase()) {
    case 'error': return 'Error'
    case 'warning':
    case 'warn': return 'Warning'
    case 'debug': return 'Debug'
    default: return 'Information'
  }
}

function getLevelSeverity(level: string): Severity {
  switch (level.toLowerCase()) {
    case 'error': return 'danger'
    case 'warning':
    case 'warn': return 'warn'
    case 'debug': return 'secondary'
    case 'info':
    case 'information': return 'info'
    default: return 'secondary'
  }
}

function getMetadataEntries(log: ApplicationEvent) {
  if (!log.metadataJson) return [] as Array<{ label: string; value: string }>

  try {
    const parsed = JSON.parse(log.metadataJson) as Record<string, unknown>
    return Object.entries(parsed)
      .filter(([, value]) => value !== null && value !== undefined && value !== '')
      .map(([key, value]) => ({
        label: humanizeMetadataKey(key),
        value: humanizeMetadataValue(key, value),
      }))
  } catch {
    return [{ label: 'Details', value: log.metadataJson }]
  }
}

function humanizeMetadataKey(key: string) {
  switch (key) {
    case 'Name': return 'Runner'
    case 'MachineName': return 'Machine'
    case 'Reason': return 'Disconnect reason'
    case 'RunnerId': return 'Runner'
    case 'VersionNumber': return 'Version'
    default: return key.replace(/([a-z0-9])([A-Z])/g, '$1 $2')
  }
}

function humanizeMetadataValue(key: string, value: unknown) {
  if (typeof value !== 'string') return JSON.stringify(value)
  if (key !== 'Reason') return value
  switch (value) {
    case 'graceful_disconnect': return 'Disconnected normally'
    case 'heartbeat_timeout': return 'Heartbeat timeout'
    default: return value.replace(/_/g, ' ')
  }
}

function formatMetadataBlock(log: ApplicationEvent) {
  return JSON.stringify(
    Object.fromEntries(getMetadataEntries(log).map(({ label, value }) => [label, value])),
    null,
    2,
  )
}

onMounted(loadActivity)
watch(() => props.appId, () => {
  filter.value.page = 1
  void loadActivity()
})

defineExpose({ loadLogs: loadActivity })
</script>

<template>
  <section class="app-runtime-logs" aria-labelledby="logs-title">
    <header class="logs-header">
      <div>
        <p class="eyebrow">Application operations</p>
        <h1 id="logs-title">Logs</h1>
        <p class="logs-summary">Runtime output and app-wide activity for {{ appName }}.</p>
      </div>
      <Select
        v-if="targets.length > 1"
        :model-value="selectedTarget?.slug"
        :options="targetOptions"
        option-label="label"
        option-value="value"
        aria-label="Environment for live runtime logs"
        class="logs-environment-select"
        @update:model-value="selectTarget"
      >
        <template #value>
        <span>{{ selectedTarget ? getEnvironmentDisplayName(selectedTarget) : 'No environment' }}</span>
        </template>
      </Select>
    </header>

    <div class="runtime-status-row" role="status" aria-label="Live runtime status">
      <div class="runtime-status-copy">
        <span class="environment-name">{{ selectedTarget ? getEnvironmentDisplayName(selectedTarget) : 'No environment' }}</span>
        <Tag :value="runtimeStatus.label" :severity="runtimeStatus.severity" />
      </div>
      <div class="runtime-status-actions">
        <Button
          v-for="view in runtimeStatus.actions"
          :key="view"
          :label="getNavigationLabel(view)"
          icon="pi pi-arrow-right"
          icon-pos="right"
          text
          size="small"
          @click="navigate(view)"
        />
      </div>
    </div>

    <Card class="logs-card live-logs-card">
      <template #title>
        <div class="card-heading">
          <div>
            <h2>Live runtime logs</h2>
            <p>Current app-page session · up to 100 recent entries · not persisted</p>
          </div>
          <span class="entry-count">{{ visibleRuntimeLogs.length }} entries</span>
        </div>
      </template>
      <template #content>
        <div class="runtime-log-viewer" aria-live="polite" aria-label="Live runtime log output">
          <Message
            v-if="runtimeStatus.unavailable"
            :severity="runtimeStatus.noticeSeverity"
            :closable="false"
            class="runtime-unavailable-message"
            aria-label="Live runtime log availability"
          >
            {{ runtimeStatus.description }}
          </Message>
          <div v-if="visibleRuntimeLogs.length > 0" class="runtime-log-list">
            <article v-for="entry in visibleRuntimeLogs" :key="entry.id" class="runtime-log-entry">
              <div class="runtime-log-meta">
                <time :datetime="entry.timestamp.toISOString()" :title="entry.timestamp.toLocaleString()">
                  {{ formatLogTime(entry.timestamp) }}
                </time>
                <Tag :value="formatLevel(entry.level ?? 'info')" :severity="getLevelSeverity(entry.level ?? 'info')" />
                <Tag v-if="entry.stream" :value="entry.stream" severity="secondary" />
                <span v-if="entry.stepName && entry.stepName !== 'backend'" class="runtime-log-context">
                  {{ entry.stepName }}
                </span>
              </div>
              <pre class="runtime-log-message">{{ entry.result.output ?? entry.result.error ?? '' }}</pre>
            </article>
          </div>
          <div v-else-if="runtimeStatus.label === 'Connecting' || runtimeStatus.label === 'Reconnecting'" class="logs-empty-state">
            <ProgressSpinner style="width: 28px; height: 28px" aria-label="Connecting to live runtime logs" />
            <p>{{ runtimeStatus.description }}</p>
          </div>
          <div v-else-if="!runtimeStatus.unavailable" class="logs-empty-state">
            <i class="pi pi-align-left" aria-hidden="true"></i>
            <p>No log output yet.</p>
            <span>Output will appear here while this app page is open and the live stream is connected.</span>
          </div>
        </div>
      </template>
    </Card>

    <Card class="logs-card activity-card">
      <template #title>
        <div class="card-heading activity-heading">
          <div>
            <h2>Application activity</h2>
            <p>Persisted lifecycle events for this app. These are app-wide and have no environment attribution; they are not runtime stdout/stderr.</p>
          </div>
          <div class="activity-controls">
            <span class="entry-count">{{ totalCount }} events</span>
            <Button
              label="Refresh"
              icon="pi pi-refresh"
              text
              size="small"
              :loading="isLoadingActivity"
              @click="loadActivity"
            />
          </div>
        </div>
      </template>
      <template #content>
        <div v-if="isLoadingActivity && !hasLoadedActivity" class="activity-loading" role="status">
          <ProgressSpinner style="width: 28px; height: 28px" aria-label="Loading application activity" />
          <span>Loading application activity…</span>
        </div>
        <Message v-else-if="activityError" severity="error" :closable="false" class="activity-error" aria-label="Application activity error">
          Application activity could not be loaded. Try refreshing, or check the app's operational status.
          <Button label="Retry" icon="pi pi-refresh" text size="small" :loading="isLoadingActivity" @click="loadActivity" />
        </Message>
        <div v-else-if="activity.length === 0" class="activity-empty-state">
          <i class="pi pi-history" aria-hidden="true"></i>
          <p>No application activity recorded yet.</p>
          <span>Deployment and publishing lifecycle events will appear here when available.</span>
        </div>
        <div v-else class="activity-list">
          <article v-for="event in activity" :key="event.id" class="activity-row" :class="{ expanded: expandedActivityId === event.id }">
            <button
              class="activity-summary"
              type="button"
              :aria-expanded="expandedActivityId === event.id"
              :aria-label="`${formatLevel(event.level)} ${formatCategory(event.category)} activity: ${event.message}`"
              @click="toggleActivity(event.id)"
            >
              <i class="pi" :class="expandedActivityId === event.id ? 'pi-chevron-down' : 'pi-chevron-right'" aria-hidden="true"></i>
              <Tag :value="formatLevel(event.level)" :severity="getLevelSeverity(event.level)" />
              <Tag :value="formatCategory(event.category)" severity="secondary" />
              <span class="activity-message">{{ event.message }}</span>
              <time class="activity-time" :datetime="event.createdAtUtc">{{ formatDate(event.createdAtUtc) }}</time>
            </button>

            <div v-if="expandedActivityId === event.id" class="activity-detail">
              <dl class="activity-detail-grid">
                <dt>Event type</dt><dd>{{ event.eventType }}</dd>
                <dt>Level</dt><dd>{{ formatLevel(event.level) }}</dd>
                <dt>Category</dt><dd>{{ formatCategory(event.category) }}</dd>
                <dt>Recorded</dt><dd>{{ formatDate(event.createdAtUtc) }}</dd>
              </dl>
              <div v-if="getMetadataEntries(event).length > 0" class="activity-metadata">
                <span>Details</span>
                <pre>{{ formatMetadataBlock(event) }}</pre>
              </div>
            </div>
          </article>
        </div>

        <nav v-if="totalPages > 1" class="activity-pagination" aria-label="Application activity pages">
          <Button icon="pi pi-chevron-left" text size="small" aria-label="Previous page" :disabled="(filter.page ?? 1) <= 1 || isLoadingActivity" @click="prevPage" />
          <span>Page {{ filter.page }} of {{ totalPages }}</span>
          <Button icon="pi pi-chevron-right" text size="small" aria-label="Next page" :disabled="(filter.page ?? 1) >= totalPages || isLoadingActivity" @click="nextPage" />
        </nav>
      </template>
    </Card>
  </section>
</template>

<style scoped>
.app-runtime-logs {
  box-sizing: border-box;
  display: flex;
  flex-direction: column;
  gap: 1rem;
  height: 100%;
  max-width: 1400px;
  margin: 0 auto;
  padding: 1.5rem;
  overflow-y: auto;
}

.logs-header,
.runtime-status-row,
.card-heading,
.activity-controls,
.runtime-status-actions {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
}

.logs-header h1,
.card-heading h2 {
  margin: 0;
}

.eyebrow {
  margin: 0 0 0.35rem;
  color: var(--p-text-muted-color);
  font-size: 0.75rem;
  font-weight: 600;
  letter-spacing: 0.04em;
  text-transform: uppercase;
}

.logs-summary,
.card-heading p {
  margin: 0.35rem 0 0;
  color: var(--p-text-muted-color);
}

.logs-environment-select {
  min-width: 13rem;
}

.runtime-status-row {
  flex-wrap: wrap;
  padding: 0.85rem 1rem;
  border: 1px solid var(--p-content-border-color);
  border-radius: 0.75rem;
  background: var(--p-content-background);
}

.runtime-status-copy {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 0.65rem;
  min-width: 0;
}

.environment-name {
  font-weight: 600;
}

.runtime-status-actions {
  justify-content: flex-end;
  gap: 0.25rem;
}

.runtime-unavailable-message {
  margin: 0;
}

.logs-card {
  flex: 0 0 auto;
  overflow: hidden;
}

.card-heading {
  align-items: flex-start;
}

.card-heading h2 {
  font-size: 1.15rem;
  font-weight: 600;
}

.entry-count {
  flex-shrink: 0;
  color: var(--p-text-muted-color);
  font-size: 0.85rem;
  font-weight: 400;
}

.runtime-log-viewer {
  max-height: 34rem;
  min-height: 9rem;
  overflow: auto;
  border: 1px solid var(--p-content-border-color);
  border-radius: 0.75rem;
  background: var(--p-content-background);
  color: var(--p-text-color);
}

.runtime-log-list {
  display: flex;
  flex-direction: column;
}

.runtime-log-entry {
  min-width: 0;
  padding: 0.65rem 0.8rem;
  border-bottom: 1px solid var(--p-content-border-color);
}

.runtime-log-entry:last-child {
  border-bottom: 0;
}

.runtime-log-meta {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 0.45rem;
  color: var(--p-text-muted-color);
  font-size: 0.78rem;
}

.runtime-log-meta time,
.runtime-log-message {
  font-family: monospace;
  font-variant-numeric: tabular-nums;
}

.runtime-log-context {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.runtime-log-message {
  width: 100%;
  margin: 0.45rem 0 0;
  overflow-x: auto;
  color: var(--p-text-color);
  font-size: 0.84rem;
  line-height: 1.5;
  white-space: pre;
}

.logs-empty-state,
.activity-loading,
.activity-empty-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 0.55rem;
  min-height: 9rem;
  padding: 1.5rem;
  color: var(--p-text-muted-color);
  text-align: center;
}

.logs-empty-state i,
.activity-empty-state i {
  font-size: 1.35rem;
}

.logs-empty-state p,
.activity-empty-state p {
  margin: 0;
  color: var(--p-text-color);
  font-weight: 600;
}

.activity-heading {
  gap: 1.5rem;
}

.activity-heading p {
  max-width: 55rem;
  line-height: 1.45;
}

.activity-controls {
  flex-shrink: 0;
  justify-content: flex-end;
}

.activity-error {
  margin: 0;
}

.activity-list {
  border-top: 1px solid var(--p-content-border-color);
}

.activity-row {
  border-bottom: 1px solid var(--p-content-border-color);
}

.activity-summary {
  display: flex;
  align-items: flex-start;
  gap: 0.55rem;
  width: 100%;
  padding: 0.7rem 0.25rem;
  border: 0;
  background: transparent;
  color: var(--p-text-color);
  cursor: pointer;
  text-align: left;
}

.activity-summary:hover {
  background: var(--p-content-background);
  border: 1px solid var(--p-content-border-color);
}

.activity-summary > i {
  flex: 0 0 auto;
  margin-top: 0.25rem;
  color: var(--p-text-muted-color);
  font-size: 0.7rem;
}

.activity-message {
  flex: 1 1 auto;
  min-width: 0;
  overflow-wrap: anywhere;
  white-space: pre-wrap;
}

.activity-time {
  flex: 0 0 auto;
  color: var(--p-text-muted-color);
  font-size: 0.8rem;
}

.activity-detail {
  margin: 0 0.25rem 0.75rem 1.5rem;
  padding: 0.75rem;
  border-radius: 0.75rem;
  background: var(--p-content-background);
}

.activity-detail-grid {
  display: grid;
  grid-template-columns: max-content minmax(0, 1fr);
  gap: 0.3rem 0.8rem;
  margin: 0;
  font-size: 0.85rem;
}

.activity-detail-grid dt {
  color: var(--p-text-muted-color);
  font-weight: 600;
}

.activity-detail-grid dd {
  margin: 0;
  overflow-wrap: anywhere;
}

.activity-metadata {
  margin-top: 0.75rem;
  color: var(--p-text-muted-color);
  font-size: 0.8rem;
  font-weight: 600;
}

.activity-metadata pre {
  max-height: 14rem;
  margin: 0.35rem 0 0;
  padding: 0.65rem;
  overflow: auto;
  border-radius: 0.75rem;
  background: var(--p-content-background);
  border: 1px solid var(--p-content-border-color);
  color: var(--p-text-color);
  font-size: 0.78rem;
  font-weight: 400;
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}

.activity-pagination {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0.75rem;
  padding-top: 0.65rem;
  color: var(--p-text-muted-color);
  font-size: 0.85rem;
}

@media (max-width: 760px) {
  .app-runtime-logs {
    padding: 1rem;
  }

  .logs-header,
  .activity-heading {
    align-items: stretch;
    flex-direction: column;
  }

  .logs-environment-select {
    width: 100%;
  }

  .runtime-status-row {
    align-items: flex-start;
    flex-direction: column;
  }

  .runtime-status-actions {
    flex-wrap: wrap;
    justify-content: flex-start;
  }

  .activity-controls {
    justify-content: space-between;
  }

  .activity-summary {
    flex-wrap: wrap;
  }

  .activity-time {
    flex-basis: 100%;
    padding-left: 1.25rem;
  }
}
</style>
