<script setup lang="ts">
import { computed } from 'vue'
import Button from 'primevue/button'
import Card from 'primevue/card'
import Message from 'primevue/message'
import Select from 'primevue/select'
import Tag from 'primevue/tag'
import type { App, AppDeploymentTarget, DeploymentTargetStatus } from '../types/apps.types'
import type { AppViewKey } from '../app-navigation'
import CopyCommand from './CopyCommand.vue'
import { getEnvironmentDisplayName } from '../utils/environment-display'
import { getAppOperationalSummary } from '../utils/app-operational-summary'
import { getDeploymentTargetUrl } from '../utils/public-app-url'
import { getViewingTargetPreviewState } from '../utils/viewing-target'

const props = defineProps<{
  app: Pick<App, 'id' | 'name'>
  targets: ReadonlyArray<AppDeploymentTarget>
  selectedTarget: AppDeploymentTarget | null
}>()

const emit = defineEmits<{
  selectTarget: [target: AppDeploymentTarget]
  navigate: [view: AppViewKey]
}>()

type Severity = 'secondary' | 'info' | 'success' | 'warn' | 'danger'

interface RuntimeNotice {
  title: string
  detail: string
  severity: 'info' | 'warn' | 'error'
}

const target = computed(() => props.selectedTarget)
const hasAssignment = computed(() =>
  !!target.value?.runner && target.value.status !== 'Unassigned',
)
const isRuntimeOnline = computed(() =>
  !!hasAssignment.value &&
  !!target.value?.runner?.isOnline &&
  target.value.status !== 'RunnerOffline',
)
const isRuntimeOffline = computed(() =>
  !!hasAssignment.value && !isRuntimeOnline.value,
)
const hasDeployedRuntime = computed(() => !!target.value?.genericRuntimeDeployed)
const operationalSummary = computed(() => getAppOperationalSummary(target.value))

const assignmentLabel = computed(() => {
  if (!hasAssignment.value) return 'No runtime assigned'
  return target.value?.runner?.isHostedRunner
    ? 'Automaton-hosted runtime'
    : 'Self-hosted runtime'
})

const availabilityLabel = computed(() => {
  if (!hasAssignment.value) return 'Not assigned'
  return isRuntimeOnline.value ? 'Online' : 'Offline'
})

const availabilitySeverity = computed<Severity>(() => {
  if (!hasAssignment.value) return 'secondary'
  return isRuntimeOnline.value ? 'success' : 'warn'
})

const hasContradictoryState = computed(() => {
  const current = target.value
  if (!current) return false

  return (
    (!!current.runner && current.status === 'Unassigned') ||
    (current.status === 'Running' && (!hasAssignment.value || !current.genericRuntimeDeployed || !current.runner?.isOnline)) ||
    (current.status === 'RunnerOffline' && (!hasAssignment.value || current.runner?.isOnline === true)) ||
    (current.genericRuntimeDeployed && !hasAssignment.value) ||
    (current.genericRuntimeDeployed && current.status === 'NotDeployed')
  )
})

function targetStatusLabel(status: DeploymentTargetStatus): string {
  switch (status) {
    case 'NotDeployed': return 'Not deployed'
    case 'Deploying': return 'Deployment in progress'
    case 'Running': return 'Running'
    case 'Failed': return 'Deployment failed'
    case 'RunnerOffline': return 'Runtime offline'
    case 'Unassigned': return 'Unassigned'
  }
}

const targetStatusSeverity = computed<Severity>(() => {
  if (hasContradictoryState.value) return 'warn'
  switch (target.value?.status) {
    case 'Running': return 'success'
    case 'Deploying': return 'info'
    case 'Failed': return 'danger'
    case 'RunnerOffline': return 'warn'
    default: return 'secondary'
  }
})

const summaryLabel = computed(() => {
  if (hasContradictoryState.value) return 'Needs review'
  if (target.value?.cleanupPending) return 'Needs attention'
  return operationalSummary.value.label
})

const summarySeverity = computed<Severity>(() => {
  if (hasContradictoryState.value || target.value?.cleanupPending) return 'warn'
  return operationalSummary.value.severity
})

const runtimeNotice = computed<RuntimeNotice | null>(() => {
  const current = target.value
  if (!current) return null

  if (hasContradictoryState.value) {
    const runtimeFact = !hasAssignment.value
      ? 'no runtime is assigned'
      : isRuntimeOnline.value
        ? 'the assigned runtime reports online'
        : 'the assigned runtime reports offline'
    const deployedFact = current.genericRuntimeDeployed
      ? 'an app-runtime record is present'
      : 'no app-runtime record is present'

    return {
      title: 'Runtime state needs review',
      detail: `The deployment target reports ${targetStatusLabel(current.status)}, while ${runtimeFact} and ${deployedFact}. Review the target in Deployments.`,
      severity: 'warn',
    }
  }

  if (current.cleanupPending) {
    return {
      title: 'Runtime cleanup needs attention',
      detail: 'Studio reports that cleanup for this deployment target is still pending. Review Deployments for the next step.',
      severity: 'warn',
    }
  }

  if (current.status === 'Failed') {
    return {
      title: 'The latest deployment failed',
      detail: 'Review this environment in Deployments and inspect Logs for recent activity.',
      severity: 'error',
    }
  }

  if (isRuntimeOffline.value) {
    return {
      title: 'The assigned runtime is offline',
      detail: 'Runtime operations are unavailable until the assigned runtime reconnects.',
      severity: 'warn',
    }
  }

  if (!hasAssignment.value) {
    return {
      title: 'No runtime is assigned to this environment',
      detail: 'Assign a runtime in Deployments before deploying the app.',
      severity: 'info',
    }
  }

  if (current.status === 'Deploying') {
    return {
      title: 'Deployment is in progress',
      detail: 'Check Deployments for the current operation state.',
      severity: 'info',
    }
  }

  return null
})

const targetOptions = computed(() => props.targets.map((deploymentTarget) => ({
  label: `${getEnvironmentDisplayName(deploymentTarget)} · ${targetStatusLabel(deploymentTarget.status)}`,
  value: deploymentTarget.slug,
  target: deploymentTarget,
})))

const canOpenApp = computed(() => {
  const current = target.value
  return !!current &&
    hasAssignment.value &&
    !hasContradictoryState.value &&
    getViewingTargetPreviewState(current).available
})

const openAppUnavailableReason = computed(() => {
  if (!target.value) return 'No deployment environment is selected.'
  if (hasContradictoryState.value) return 'Runtime state needs review before opening the app.'
  return getViewingTargetPreviewState(target.value).message ?? 'The app is unavailable.'
})

const showCliGuidance = computed(() =>
  !!target.value &&
  hasAssignment.value &&
  isRuntimeOnline.value &&
  !hasDeployedRuntime.value &&
  !hasContradictoryState.value &&
  target.value.status !== 'Deploying' &&
  operationalSummary.value.cliGuidance !== 'hidden',
)

function getAppUrl(): string | null {
  const current = target.value
  if (!current || !canOpenApp.value) return null
  const path = getDeploymentTargetUrl(props.app.id, current.routePath)
  if (!path) return null
  return new URL(path, window.location.origin).toString()
}

function openApp(): void {
  const url = getAppUrl()
  if (url) window.open(url, '_blank', 'noopener')
}

function selectTarget(slug: string | null | undefined): void {
  const selected = props.targets.find((candidate) => candidate.slug === slug)
  if (selected) emit('selectTarget', selected)
}

function navigate(view: AppViewKey): void {
  emit('navigate', view)
}
</script>

<template>
  <section class="runtime-operations" aria-labelledby="runtime-title">
    <header class="runtime-header">
      <div>
        <p class="eyebrow">Application operations</p>
        <h1 id="runtime-title">Runtime</h1>
        <p class="runtime-summary">Runtime assignment and availability for {{ app.name }}.</p>
      </div>
      <Select
        v-if="targets.length > 1"
        :model-value="selectedTarget?.slug"
        :options="targetOptions"
        option-label="label"
        option-value="value"
        aria-label="Environment"
        class="runtime-environment-select"
        @update:model-value="selectTarget"
      >
        <template #value>
          <span>{{ getEnvironmentDisplayName(selectedTarget) }}</span>
        </template>
      </Select>
    </header>

    <Message v-if="!selectedTarget" severity="secondary" :closable="false" class="runtime-empty">
      <div class="runtime-notice">
        <div>
          <h2>No deployment environment is available</h2>
          <p>Create or assign an environment before deploying this app.</p>
        </div>
        <Button label="View deployments" icon="pi pi-arrow-right" icon-pos="right" text @click="navigate('deployments')" />
      </div>
    </Message>

    <template v-else>
      <Card class="runtime-summary-card">
        <template #title>
          <div class="runtime-card-heading">
            <div>
              <p class="eyebrow">Selected environment</p>
              <h2>{{ getEnvironmentDisplayName(selectedTarget) }}</h2>
            </div>
            <Tag :value="summaryLabel" :severity="summarySeverity" />
          </div>
        </template>
        <template #content>
          <dl class="runtime-facts">
            <div>
              <dt>Runtime assignment</dt>
              <dd>{{ assignmentLabel }}</dd>
            </div>
            <div>
              <dt>Runtime availability</dt>
              <dd><Tag :value="availabilityLabel" :severity="availabilitySeverity" /></dd>
            </div>
            <div>
              <dt>Application runtime record</dt>
              <dd><Tag :value="hasDeployedRuntime ? 'Present' : 'Not present'" :severity="hasDeployedRuntime ? 'success' : 'secondary'" /></dd>
            </div>
            <div>
              <dt>Deployment target status</dt>
              <dd>
                <Tag
                  :value="`${targetStatusLabel(selectedTarget.status)}${hasContradictoryState ? ' · needs review' : ''}`"
                  :severity="targetStatusSeverity"
                />
              </dd>
            </div>
          </dl>
        </template>
      </Card>

      <Message
        v-if="runtimeNotice"
        :severity="runtimeNotice.severity"
        :closable="false"
        class="runtime-notice-message"
        aria-label="Runtime status details"
      >
        <div class="runtime-notice">
          <div>
            <p class="eyebrow">{{ hasContradictoryState ? 'Needs attention' : 'Runtime details' }}</p>
            <h2>{{ runtimeNotice.title }}</h2>
            <p>{{ runtimeNotice.detail }}</p>
          </div>
        </div>
      </Message>

      <Card class="runtime-actions-card">
        <template #title>Runtime actions</template>
        <template #content>
          <p class="muted">{{ canOpenApp ? 'Open the deployed app or inspect its operational details.' : 'Inspect this environment and its deployment state.' }}</p>
          <div class="runtime-actions">
            <Button
              label="Open app"
              icon="pi pi-external-link"
              outlined
              :disabled="!canOpenApp"
              :title="canOpenApp ? undefined : openAppUnavailableReason"
              @click="openApp"
            />
            <Button v-if="hasAssignment" label="View logs" icon="pi pi-list" text @click="navigate('logs')" />
            <Button label="View deployments" icon="pi pi-cloud-upload" text @click="navigate('deployments')" />
          </div>
        </template>
      </Card>

      <Message v-if="showCliGuidance" severity="info" :closable="false" class="runtime-cli-guidance">
        <div>
          <p class="eyebrow">Source deployment stays local</p>
          <p>This environment has no app runtime deployed. Build locally, then deploy with Automaton CLI.</p>
          <CopyCommand :command="`automaton deploy ${selectedTarget.slug}`" button-label="Copy deploy command" />
        </div>
      </Message>
    </template>
  </section>
</template>

<style scoped>
.runtime-operations {
  height: 100%;
  max-width: 1400px;
  margin: 0 auto;
  padding: 1.5rem;
  overflow-y: auto;
}

.runtime-header,
.runtime-card-heading,
.runtime-notice,
.runtime-actions {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
}

.runtime-header {
  margin-bottom: 1.25rem;
}

.runtime-header h1,
.runtime-card-heading h2,
.runtime-notice h2 {
  margin: 0 0 0.4rem;
}

.runtime-header p,
.runtime-notice p,
.muted {
  margin: 0;
  color: var(--p-text-muted-color);
}

.eyebrow {
  margin: 0 0 0.35rem !important;
  color: var(--p-text-muted-color);
  font-size: 0.75rem;
  font-weight: 700;
  letter-spacing: 0.06em;
  text-transform: uppercase;
}

.runtime-environment-select {
  width: min(19rem, 45%);
  flex: 0 0 auto;
}

.runtime-summary-card,
.runtime-notice-message,
.runtime-actions-card,
.runtime-cli-guidance {
  margin-bottom: 1rem;
}

.runtime-card-heading {
  align-items: flex-start;
}

.runtime-card-heading h2 {
  font-size: 1.25rem;
}

.runtime-facts {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 1.25rem 2rem;
  margin: 0;
}

.runtime-facts > div {
  min-width: 0;
}

.runtime-facts dt {
  margin-bottom: 0.45rem;
  color: var(--p-text-muted-color);
  font-size: 0.875rem;
}

.runtime-facts dd {
  margin: 0;
  font-weight: 600;
}

.runtime-notice {
  align-items: flex-start;
}

.runtime-notice h2 {
  font-size: 1rem;
}

.runtime-actions-card :deep(.p-card-content),
.runtime-cli-guidance > div {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 0.85rem;
}

.runtime-actions {
  justify-content: flex-start;
  flex-wrap: wrap;
}

.runtime-cli-guidance p {
  margin: 0;
}

.runtime-cli-guidance .eyebrow {
  margin-bottom: -0.35rem !important;
}

@media (max-width: 760px) {
  .runtime-operations {
    padding: 1rem;
  }

  .runtime-header,
  .runtime-notice {
    align-items: flex-start;
    flex-direction: column;
  }

  .runtime-environment-select {
    width: 100%;
  }
}

@media (max-width: 520px) {
  .runtime-facts {
    grid-template-columns: 1fr;
    gap: 1rem;
  }

  .runtime-card-heading {
    align-items: flex-start;
    flex-direction: column;
  }
}
</style>
