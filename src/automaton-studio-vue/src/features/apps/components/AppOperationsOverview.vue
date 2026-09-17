<script setup lang="ts">
import { computed } from 'vue'
import Tag from 'primevue/tag'
import type { App, AppDeploymentTarget } from '../types/apps.types'
import type { AppViewKey } from '../app-navigation'
import type { AppOperationalAction, AppOperationalSummary } from '../utils/app-operational-summary'
import CopyCommand from './CopyCommand.vue'
import { getPublishedAppUrl, getDeploymentTargetUrl } from '../utils/public-app-url'
import { getEnvironmentDisplayName } from '../utils/environment-display'
import { getViewingTargetPreviewState } from '../utils/viewing-target'

const props = defineProps<{
  app: App
  targets: ReadonlyArray<AppDeploymentTarget>
  applicationState: AppOperationalSummary
}>()

const emit = defineEmits<{
  navigate: [view: AppViewKey]
}>()

const liveTarget = computed(() => props.targets.find((target) => target.isDefault) ?? null)
const liveTargetName = computed(() =>
  liveTarget.value ? getEnvironmentDisplayName(liveTarget.value) : 'No Live target',
)
const liveRuntimeState = computed(() => getViewingTargetPreviewState(liveTarget.value))
const liveAppUrl = computed(() => {
  const target = liveTarget.value
  if (!target || !liveRuntimeState.value.available) return null
  return getDeploymentTargetUrl(props.app.id, target.routePath) || null
})
const liveAppUnavailableReason = computed(() => {
  if (!liveTarget.value) return 'No Live deployment target is available to open.'
  if (liveRuntimeState.value.message === 'Not deployed') {
    return 'The Live app is not deployed yet, so there is no URL to open.'
  }
  if (liveRuntimeState.value.message === 'No runtime assigned') {
    return 'Assign a runtime before opening the Live app.'
  }
  if (liveRuntimeState.value.message === 'Runtime is offline') {
    return 'The runtime is offline, so the Live app cannot be opened.'
  }
  return liveRuntimeState.value.message || 'The Live app URL is unavailable.'
})
const publishedUrl = computed(() =>
  props.app.isPublished && props.app.publicUrl
    ? getPublishedAppUrl(props.app.publicUrl)
    : null,
)
const runtimeAssignment = computed(() => {
  const runner = liveTarget.value?.runner
  if (!runner) return 'Not assigned'
  return runner.isHostedRunner ? 'Automaton-hosted runtime' : 'Self-hosted runtime'
})
const publishedAccess = computed(() => {
  if (props.app.runtimeVisibility === 'private') return 'Private runtime access'
  if (props.app.requiresAuthentication) return 'Sign-in required'
  if (props.app.canLaunchPublicly === true) return 'Public access enabled'
  if (props.app.canLaunchPublicly === false) return 'Public launch unavailable'
  return 'Access details unavailable'
})
const lastSuccessfulDeploymentAt = computed(() => liveTarget.value?.lastSuccessfulDeploymentAt ?? null)
const lastDeploymentAt = computed(() => liveTarget.value?.lastDeploymentAt ?? null)
const showLastAttempt = computed(() =>
  !!lastDeploymentAt.value && props.applicationState.status === 'deployment-failed',
)
const hasPrimaryCliGuidance = computed(() => props.applicationState.cliGuidance === 'primary')
const hasCliGuidance = computed(() => props.applicationState.cliGuidance !== 'hidden')
const deploymentSeverity = computed(() => {
  if (props.applicationState.status === 'operation-in-progress') return 'info'
  if (props.applicationState.status === 'deployment-failed') return 'danger'
  return props.applicationState.hasDeployedRuntime ? 'success' : 'secondary'
})

function formatDate(value: string): string {
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? 'Date unavailable' : date.toLocaleString()
}

function navigate(action: AppOperationalAction): void {
  emit('navigate', action.view)
}
</script>

<template>
  <section class="operations-overview" aria-labelledby="operations-overview-title">
    <header class="overview-header">
      <div>
        <p class="eyebrow">Application operations</p>
        <h1 id="operations-overview-title">Overview</h1>
        <p class="overview-summary">Live deployment, runtime, and publishing for {{ app.name }}.</p>
      </div>
    </header>

    <section class="live-summary" aria-labelledby="live-summary-title">
      <div class="live-summary__heading">
        <div>
          <p class="eyebrow">Current deployment</p>
          <h2 id="live-summary-title">{{ liveTargetName }}</h2>
        </div>
        <Tag :value="applicationState.deploymentLabel" :severity="deploymentSeverity" />
      </div>

      <div class="live-summary__facts">
        <p v-if="showLastAttempt && lastDeploymentAt" class="deployment-time">
          Last attempt <time :datetime="lastDeploymentAt">{{ formatDate(lastDeploymentAt) }}</time>
        </p>
        <p v-if="lastSuccessfulDeploymentAt" class="deployment-time">
          Last successful deployment
          <time :datetime="lastSuccessfulDeploymentAt">{{ formatDate(lastSuccessfulDeploymentAt) }}</time>
        </p>
        <p v-else class="deployment-time">No successful deployment yet.</p>
      </div>

      <div class="live-app-link">
        <div class="live-app-link__url">
          <span class="status-label">Live app URL</span>
          <a v-if="liveAppUrl" :href="liveAppUrl" target="_blank" rel="noopener noreferrer">
            {{ liveAppUrl }}
          </a>
          <span v-else class="muted">{{ liveAppUnavailableReason }}</span>
        </div>
        <a
          v-if="liveAppUrl"
          class="open-app-link"
          :href="liveAppUrl"
          target="_blank"
          rel="noopener noreferrer"
        >
          Open app
          <i class="pi pi-external-link" aria-hidden="true"></i>
        </a>
      </div>
    </section>

    <div class="summary-grid">
      <article class="summary-card">
        <span class="status-label">Runtime</span>
        <strong>{{ applicationState.runtimeLabel }}</strong>
        <span class="muted">{{ runtimeAssignment }}</span>
        <button type="button" @click="emit('navigate', 'runtime')">View runtime</button>
      </article>

      <article class="summary-card">
        <span class="status-label">Publishing</span>
        <strong>{{ app.isPublished ? 'Published' : 'Unpublished' }}</strong>
        <template v-if="app.isPublished">
          <span class="muted">{{ publishedAccess }}</span>
          <a v-if="publishedUrl" class="published-link" :href="publishedUrl" target="_blank" rel="noopener noreferrer">
            Open published app
            <i class="pi pi-external-link" aria-hidden="true"></i>
          </a>
          <span v-else class="muted">No published URL is configured.</span>
        </template>
        <span v-else class="muted">Deployment and publishing are separate.</span>
        <button type="button" @click="emit('navigate', 'publishing')">Manage publishing</button>
      </article>
    </div>

    <section v-if="applicationState.diagnostic" class="diagnostic" aria-label="Recent operational issue">
      <div class="diagnostic__copy">
        <p class="eyebrow">Needs attention</p>
        <h2>{{ applicationState.diagnostic.title }}</h2>
        <p>{{ applicationState.diagnostic.detail }}</p>
      </div>
      <div class="diagnostic__actions">
        <button type="button" class="diagnostic__primary" @click="navigate(applicationState.diagnostic.action)">
          {{ applicationState.diagnostic.action.label }}
        </button>
        <button
          v-if="applicationState.diagnostic.secondaryAction"
          type="button"
          class="diagnostic__secondary"
          @click="navigate(applicationState.diagnostic.secondaryAction)"
        >
          {{ applicationState.diagnostic.secondaryAction.label }}
        </button>
      </div>
    </section>

    <aside
      v-if="hasCliGuidance"
      class="local-source-card"
      :class="{ 'local-source-card--primary': hasPrimaryCliGuidance }"
    >
      <div>
        <h2>{{ hasPrimaryCliGuidance ? 'Deploy from your local project' : 'Source changes stay local' }}</h2>
        <p v-if="hasPrimaryCliGuidance">
          This app has no deployed runtime. Run Automaton CLI from its linked project to deploy it.
        </p>
        <p v-else>
          When you need to change source, use <code>automaton pull</code>, edit locally, then deploy with Automaton CLI.
        </p>
      </div>
      <CopyCommand class="command-row" command="automaton deploy" button-label="Copy automaton deploy" />
    </aside>
  </section>
</template>

<style scoped>
.operations-overview {
  height: 100%;
  padding: 1.25rem;
  overflow-y: auto;
}

.overview-header {
  margin-bottom: 0.9rem;
}

.eyebrow,
.status-label {
  color: var(--p-text-muted-color);
  font-size: 0.75rem;
  font-weight: 700;
  letter-spacing: 0.06em;
  text-transform: uppercase;
}

h1,
h2,
p {
  margin-top: 0;
}

h1 {
  margin-bottom: 0.4rem;
}

.overview-summary,
.muted,
.live-summary__facts {
  color: var(--p-text-muted-color);
}

.overview-summary,
.live-summary__facts p {
  margin-bottom: 0;
}

.live-summary,
.summary-card,
.diagnostic,
.local-source-card {
  border: 1px solid var(--p-surface-border);
  border-radius: 0.9rem;
  background: var(--p-surface-card);
}

.live-summary {
  display: flex;
  flex-direction: column;
  gap: 0.65rem;
  padding: 1rem 1.15rem;
  border-left: 3px solid var(--p-primary-color);
}

.live-summary__heading,
.live-app-link,
.summary-grid,
.diagnostic,
.diagnostic__actions,
.local-source-card {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
}

.live-summary__heading h2 {
  margin-bottom: 0;
  font-size: 1.1rem;
}

.live-summary__heading .eyebrow {
  margin-bottom: 0.35rem;
}

.live-summary__facts {
  display: flex;
  flex-wrap: wrap;
  gap: 0.35rem 1.5rem;
  font-size: 0.82rem;
}

.deployment-time time {
  margin-left: 0.35rem;
  color: var(--p-text-color);
}

.live-app-link {
  align-items: center;
  padding-top: 0.65rem;
  border-top: 1px solid var(--p-surface-border);
}

.live-app-link__url {
  display: flex;
  min-width: 0;
  flex-direction: column;
  gap: 0.35rem;
}

.live-app-link__url a,
.published-link,
.open-app-link,
.summary-card button,
.diagnostic button {
  color: var(--p-primary-color);
  font: inherit;
  font-weight: 600;
}

.live-app-link__url a {
  overflow-wrap: anywhere;
}

.open-app-link,
.published-link {
  display: inline-flex;
  align-items: center;
  gap: 0.45rem;
  text-decoration: none;
  white-space: nowrap;
}

.summary-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  align-items: stretch;
  gap: 0.75rem;
  margin-top: 0.75rem;
}

.summary-card {
  display: flex;
  min-width: 0;
  align-items: flex-start;
  flex-direction: column;
  gap: 0.4rem;
  padding: 0.85rem 1rem;
  border-left: 2px solid var(--p-primary-color);
  background: color-mix(in srgb, var(--p-primary-color) 3%, var(--p-surface-card));
}

.summary-card strong {
  font-size: 1.05rem;
}

.summary-card > .muted {
  font-size: 0.85rem;
  overflow-wrap: anywhere;
}

.summary-card button,
.diagnostic button {
  border: 0;
  background: none;
  padding: 0;
  cursor: pointer;
}

.summary-card button {
  margin-top: 0.15rem;
}

.diagnostic {
  margin-top: 0.75rem;
  padding: 0.8rem 1rem;
  border-color: var(--p-orange-300);
  border-left: 3px solid var(--p-orange-500);
  background: color-mix(in srgb, var(--p-orange-300) 7%, var(--p-surface-card));
}

.diagnostic__copy .eyebrow {
  margin-bottom: 0.3rem;
}

.diagnostic__copy h2 {
  margin-bottom: 0.35rem;
  font-size: 1rem;
}

.diagnostic__copy p:last-child {
  margin-bottom: 0;
  color: var(--p-text-muted-color);
  font-size: 0.88rem;
}

.diagnostic__actions {
  flex-shrink: 0;
}

.diagnostic__secondary {
  color: var(--p-text-muted-color) !important;
}

.local-source-card {
  margin-top: 0.75rem;
  padding: 0.75rem 1rem;
}

.local-source-card--primary {
  border-color: var(--p-primary-color);
  padding: 1rem 1.15rem;
}

.local-source-card h2 {
  margin-bottom: 0.35rem;
  font-size: 1rem;
}

.local-source-card p {
  margin-bottom: 0;
  color: var(--p-text-muted-color);
  font-size: 0.88rem;
}

.local-source-card code {
  color: var(--p-text-color);
}

.command-row {
  display: flex;
  flex-shrink: 0;
  align-items: center;
  gap: 0.8rem;
  padding: 0.65rem 0.8rem;
  border-radius: 0.7rem;
  background: var(--p-surface-ground);
  white-space: nowrap;
}

@media (max-width: 760px) {
  .summary-grid {
    grid-template-columns: 1fr;
  }
}

@media (max-width: 720px) {
  .live-summary__heading,
  .live-app-link,
  .diagnostic,
  .local-source-card {
    align-items: stretch;
    flex-direction: column;
  }

  .live-app-link {
    align-items: flex-start;
  }

  .diagnostic__actions {
    flex-wrap: wrap;
  }

  .summary-grid {
    grid-template-columns: 1fr;
  }

  .command-row {
    justify-content: space-between;
  }
}
</style>
