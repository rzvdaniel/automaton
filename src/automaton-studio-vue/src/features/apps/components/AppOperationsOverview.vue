<script setup lang="ts">
import { computed, ref } from 'vue'
import type { App, AppDeploymentTarget } from '../types/apps.types'
import type { AppViewKey } from '../app-navigation'
import { getPublishedAppUrl } from '../utils/public-app-url'
import { getEnvironmentDisplayName } from '../utils/environment-display'

const props = defineProps<{
  app: App
  targets: ReadonlyArray<AppDeploymentTarget>
  applicationState: 'Deployed' | 'Source changed' | 'Build failed' | 'Runner offline' | 'Not deployed'
}>()

const emit = defineEmits<{
  navigate: [view: AppViewKey]
}>()

const copied = ref(false)
const liveTarget = computed(() => props.targets.find((target) => target.isDefault) ?? null)
const publishedUrl = computed(() =>
  props.app.isPublished && props.app.publicUrl
    ? getPublishedAppUrl(props.app.publicUrl)
    : null,
)

const runtimeLabel = computed(() => {
  const target = liveTarget.value
  if (!target) return 'No Live environment'
  if (!target.runner) return 'No runtime assigned'
  if (!target.runner.isOnline) return 'Runner offline'
  if (!target.genericRuntimeDeployed) return 'Not deployed'
  return target.status === 'Running' ? 'Running' : target.status
})

const deploymentLabel = computed(() => {
  if (!liveTarget.value) return 'No deployment target'
  if (props.applicationState === 'Source changed') return 'Local changes not deployed'
  if (props.applicationState === 'Build failed') return 'Last deployment failed'
  return liveTarget.value.genericRuntimeDeployed ? 'Deployed' : 'Not deployed'
})

const lastDeploymentLabel = computed(() => {
  const value = liveTarget.value?.lastSuccessfulDeploymentAt
  if (!value) return 'No successful deployment'
  return `Last successful deployment: ${new Date(value).toLocaleString()}`
})

async function copyDeployCommand() {
  await navigator.clipboard.writeText('automaton deploy')
  copied.value = true
  window.setTimeout(() => {
    copied.value = false
  }, 2000)
}
</script>

<template>
  <section class="operations-overview" aria-labelledby="operations-overview-title">
    <header class="overview-header">
      <div>
        <p class="eyebrow">Application operations</p>
        <h1 id="operations-overview-title">Overview</h1>
        <p class="overview-summary">
          Check the Live deployment, runtime, logs, and publishing state for {{ app.name }}.
        </p>
      </div>
      <a v-if="publishedUrl" class="public-link" :href="publishedUrl" target="_blank" rel="noopener">
        Open published app
        <i class="pi pi-external-link" aria-hidden="true"></i>
      </a>
    </header>

    <div class="status-grid">
      <article class="status-card">
        <span class="status-label">Runtime</span>
        <strong>{{ runtimeLabel }}</strong>
        <span>{{ liveTarget ? getEnvironmentDisplayName(liveTarget) : 'Assign a Live environment in Deployments.' }}</span>
        <button type="button" @click="emit('navigate', 'runtime')">View runtime</button>
      </article>

      <article class="status-card">
        <span class="status-label">Deployment</span>
        <strong>{{ deploymentLabel }}</strong>
        <span>{{ lastDeploymentLabel }}</span>
        <button type="button" @click="emit('navigate', 'deployments')">View deployments</button>
      </article>

      <article class="status-card">
        <span class="status-label">Publishing</span>
        <strong>{{ app.isPublished ? 'Published' : 'Unpublished' }}</strong>
        <span>{{ publishedUrl ?? 'No public URL' }}</span>
        <button type="button" @click="emit('navigate', 'publishing')">Manage publishing</button>
      </article>

      <article class="status-card">
        <span class="status-label">Diagnosis</span>
        <strong>{{ applicationState }}</strong>
        <span>Use runtime logs to investigate application failures.</span>
        <button type="button" @click="emit('navigate', 'logs')">View logs</button>
      </article>
    </div>

    <aside class="local-source-card">
      <div>
        <h2>Your local Automaton project is the source of truth</h2>
        <p>Pull and edit the project locally with your AI. Deploy source changes with Automaton CLI.</p>
      </div>
      <div class="command-row">
        <code>automaton deploy</code>
        <button type="button" @click="copyDeployCommand">
          <i :class="copied ? 'pi pi-check' : 'pi pi-copy'" aria-hidden="true"></i>
          {{ copied ? 'Copied' : 'Copy command' }}
        </button>
      </div>
    </aside>
  </section>
</template>

<style scoped>
.operations-overview {
  height: 100%;
  padding: 1.5rem;
  overflow-y: auto;
}

.overview-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
  margin-bottom: 1.5rem;
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
.local-source-card p {
  color: var(--p-text-muted-color);
  margin-bottom: 0;
}

.public-link,
.status-card button,
.command-row button {
  color: var(--p-primary-color);
  font: inherit;
  font-weight: 600;
}

.public-link {
  display: inline-flex;
  align-items: center;
  gap: 0.5rem;
  text-decoration: none;
}

.status-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(13rem, 1fr));
  gap: 1rem;
}

.status-card,
.local-source-card {
  border: 1px solid var(--p-surface-border);
  border-radius: 0.9rem;
  background: var(--p-surface-card);
}

.status-card {
  display: flex;
  align-items: flex-start;
  flex-direction: column;
  gap: 0.55rem;
  min-height: 10rem;
  padding: 1rem;
}

.status-card strong {
  font-size: 1.05rem;
}

.status-card > span:last-of-type {
  flex: 1;
  color: var(--p-text-muted-color);
  font-size: 0.85rem;
  overflow-wrap: anywhere;
}

.status-card button,
.command-row button {
  padding: 0;
  border: 0;
  background: transparent;
  cursor: pointer;
}

.local-source-card {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1.5rem;
  margin-top: 1rem;
  padding: 1.25rem;
}

.local-source-card h2 {
  margin-bottom: 0.4rem;
  font-size: 1rem;
}

.command-row {
  display: flex;
  align-items: center;
  gap: 0.8rem;
  padding: 0.7rem 0.85rem;
  border-radius: 0.7rem;
  background: var(--p-surface-ground);
  white-space: nowrap;
}

.command-row button {
  display: inline-flex;
  align-items: center;
  gap: 0.4rem;
}

@media (max-width: 720px) {
  .overview-header,
  .local-source-card {
    align-items: stretch;
    flex-direction: column;
  }

  .command-row {
    justify-content: space-between;
  }
}
</style>
