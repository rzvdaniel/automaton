<script setup lang="ts">
import { computed } from 'vue'
import Button from 'primevue/button'
import Card from 'primevue/card'
import Message from 'primevue/message'
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
      <Card>
        <template #title>
          <div class="live-summary__heading">
            <div>
              <p class="eyebrow">Current deployment</p>
              <h2 id="live-summary-title">{{ liveTargetName }}</h2>
            </div>
            <Tag :value="applicationState.deploymentLabel" :severity="deploymentSeverity" />
          </div>
        </template>
        <template #content>
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
        </template>
      </Card>
    </section>

    <div class="summary-grid">
      <Card class="summary-card">
        <template #title>Runtime</template>
        <template #content>
          <div class="summary-card__content">
            <strong>{{ applicationState.runtimeLabel }}</strong>
            <span class="muted">{{ runtimeAssignment }}</span>
            <Button label="View runtime" icon="pi pi-arrow-right" icon-pos="right" text @click="emit('navigate', 'runtime')" />
          </div>
        </template>
      </Card>

      <Card class="summary-card">
        <template #title>Publishing</template>
        <template #content>
          <div class="summary-card__content">
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
            <Button label="Manage publishing" icon="pi pi-arrow-right" icon-pos="right" text @click="emit('navigate', 'publishing')" />
          </div>
        </template>
      </Card>
    </div>

    <Message
      v-if="applicationState.diagnostic"
      severity="warn"
      :closable="false"
      class="diagnostic"
      aria-label="Recent operational issue"
    >
      <div class="diagnostic__layout">
        <div class="diagnostic__copy">
          <p class="eyebrow">Needs attention</p>
          <h2>{{ applicationState.diagnostic.title }}</h2>
          <p>{{ applicationState.diagnostic.detail }}</p>
        </div>
        <div class="diagnostic__actions">
          <Button
            :label="applicationState.diagnostic.action.label"
            icon="pi pi-arrow-right"
            icon-pos="right"
            text
            class="diagnostic__primary"
            @click="navigate(applicationState.diagnostic.action)"
          />
          <Button
            v-if="applicationState.diagnostic.secondaryAction"
            :label="applicationState.diagnostic.secondaryAction.label"
            text
            severity="secondary"
            class="diagnostic__secondary"
            @click="navigate(applicationState.diagnostic.secondaryAction)"
          />
        </div>
      </div>
    </Message>

    <Card
      v-if="hasCliGuidance"
      class="local-source-card"
      :class="{ 'local-source-card--primary': hasPrimaryCliGuidance }"
    >
      <template #title>
        <div class="local-source-card__heading">
          <span>{{ hasPrimaryCliGuidance ? 'Deploy from your local project' : 'Source changes stay local' }}</span>
          <Tag v-if="hasPrimaryCliGuidance" value="Next step" severity="info" />
        </div>
      </template>
      <template #content>
        <div class="local-source-card__content">
          <p v-if="hasPrimaryCliGuidance">
            This app has no deployed runtime. Run Automaton CLI from its linked project to deploy it.
          </p>
          <p v-else>
            When you need to change source, use <code>automaton pull</code>, edit locally, then deploy with Automaton CLI.
          </p>
          <CopyCommand class="command-row" command="automaton deploy" button-label="Copy automaton deploy" />
        </div>
      </template>
    </Card>
  </section>
</template>

<style scoped>
.operations-overview {
  height: 100%;
  max-width: 1400px;
  margin: 0 auto;
  padding: 1.5rem;
  overflow-y: auto;
}

.overview-header {
  margin-bottom: 1.25rem;
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
  font-size: 1.875rem;
  font-weight: 700;
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

.live-summary {
  width: 100%;
}

.live-summary__heading {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.75rem;
}

.live-summary__heading h2 {
  margin: 0;
  font-size: 1.25rem;
  font-weight: 600;
}

.live-summary__heading .eyebrow {
  margin-bottom: 0.35rem;
}

.live-summary__facts {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem 1.5rem;
  font-size: 0.875rem;
}

.deployment-time time {
  margin-left: 0.35rem;
  color: var(--p-text-color);
}

.live-app-link {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 1rem;
  margin-top: 1rem;
  padding-top: 1rem;
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
.open-app-link {
  color: var(--p-primary-color);
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
  gap: 1rem;
  margin-top: 1rem;
}

.summary-card {
  min-width: 0;
}

.summary-card :deep(.p-card-body) {
  height: 100%;
}

.summary-card__content {
  display: flex;
  height: 100%;
  flex-direction: column;
  align-items: flex-start;
  gap: 0.5rem;
}

.summary-card__content strong {
  font-size: 1.05rem;
}

.summary-card__content > .muted {
  font-size: 0.85rem;
  overflow-wrap: anywhere;
}

.summary-card__content > :deep(.p-button) {
  margin-top: auto;
  padding-left: 0;
}

.diagnostic {
  display: block;
  margin-top: 1rem;
}

.diagnostic :deep(.p-message-content) {
  align-items: flex-start;
}

.diagnostic__layout {
  display: flex;
  width: 100%;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
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

.diagnostic__actions,
.local-source-card__heading {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.75rem;
}

.diagnostic__actions {
  flex-shrink: 0;
  justify-content: flex-end;
}

.local-source-card__content {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
}

.local-source-card {
  margin-top: 1rem;
}

.local-source-card__heading {
  justify-content: flex-start;
  flex-wrap: wrap;
}

.local-source-card__content p {
  margin-bottom: 0;
  color: var(--p-text-muted-color);
  font-size: 0.875rem;
}

.local-source-card code {
  color: var(--p-text-color);
}

.command-row {
  display: flex;
  flex-shrink: 0;
  align-items: center;
  gap: 0.8rem;
  padding: 0.5rem 0.65rem;
  border-radius: var(--p-border-radius);
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
  .diagnostic__layout,
  .local-source-card__content {
    align-items: stretch;
    flex-direction: column;
  }

  .live-app-link {
    align-items: flex-start;
  }

  .diagnostic__actions {
    align-self: flex-start;
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
