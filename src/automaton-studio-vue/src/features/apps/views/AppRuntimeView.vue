<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import ProgressSpinner from 'primevue/progressspinner'
import CopyCommand from '../components/CopyCommand.vue'
import { useToast } from 'primevue/usetoast'
import { appsService, type CatalogUpdate } from '../services/apps.service'
import { applyCatalogUpdate, isRetryOnly, shortReason } from '../utils/catalog-update'
import type { App } from '../types/apps.types'
import { getOpenAppUrl, withFirstPage } from '../utils/public-app-url'

const route = useRoute()
const router = useRouter()

const appId = computed(() => String(route.params.id || ''))
const app = ref<App | null>(null)
const applicationUrl = ref('')
const isLoading = ref(true)
const error = ref('')
const isNotDeployed = ref(false)
const canBeFramed = ref(true)
const toast = useToast()
/** A catalog app has no local project: when it never deployed, the way forward is its update (or retry), not the CLI. */
const isCatalogApp = computed(() => !!app.value?.installedFromRepo)
const catalogUpdate = ref<CatalogUpdate | null>(null)
const updateBusy = ref(false)
const updateLabel = computed(() => (catalogUpdate.value && isRetryOnly(catalogUpdate.value) ? 'Retry' : 'Update'))
const failureLine = computed(() => shortReason(catalogUpdate.value?.deployError))

async function loadRuntimeView() {
  if (!appId.value) {
    error.value = 'App not found.'
    isLoading.value = false
    return
  }

  isLoading.value = true
  error.value = ''
  isNotDeployed.value = false

  try {
    app.value = await appsService.get(appId.value)

    const targets = await appsService.listDeploymentTargets(appId.value)
    const liveTarget = targets.find((target) => target.isDefault)
    if (!liveTarget?.genericRuntimeDeployed) {
      catalogUpdate.value = null
      isNotDeployed.value = true
      if (isCatalogApp.value) {
        catalogUpdate.value = await appsService.getCatalogUpdate(appId.value).then((result) => (result.available ? result : null)).catch(() => null)
        error.value = catalogUpdate.value?.deployFailed ? 'The build of this app failed.' : 'This app has not been deployed yet.'
      } else {
        error.value = 'This app has not been deployed yet.'
      }
      return
    }

    // The private address always asks for sign-in, so the session cookie is always handed out.
    await appsService.createRuntimeLaunch(appId.value)

    // A failed check keeps the framed view, which is what every app had before.
    canBeFramed.value = await appsService.canBeFramed(appId.value).catch(() => true)

    applicationUrl.value = withFirstPage(new URL(getOpenAppUrl(app.value), window.location.origin).toString(), app.value)
  } catch (err) {
    error.value = err instanceof Error ? err.message : 'Failed to load application.'
  } finally {
    isLoading.value = false
  }
}

async function runUpdate() {
  if (!catalogUpdate.value || updateBusy.value) return
  updateBusy.value = true
  try {
    if (await applyCatalogUpdate(appId.value, catalogUpdate.value, toast)) await router.push(`/apps/${appId.value}`)
  } finally {
    updateBusy.value = false
  }
}

function openInNewTab() {
  window.open(applicationUrl.value, '_blank', 'noopener')
}

// The app session cookie lives 10 minutes; renew it while the app is shown and the Studio session is valid.
const RUNTIME_RENEW_MS = 4 * 60 * 1000
let renewTimer: ReturnType<typeof setInterval> | undefined
function renewRuntimeSession() {
  if (!appId.value || !applicationUrl.value) return
  // A failure (Studio session ended) is left alone: the app then shows its own "session ended".
  appsService.createRuntimeLaunch(appId.value).catch(() => undefined)
}
function onVisible() {
  if (document.visibilityState === 'visible') renewRuntimeSession()
}
onBeforeUnmount(() => {
  if (renewTimer) clearInterval(renewTimer)
  document.removeEventListener('visibilitychange', onVisible)
})

onMounted(() => {
  renewTimer = setInterval(renewRuntimeSession, RUNTIME_RENEW_MS)
  document.addEventListener('visibilitychange', onVisible)
  void loadRuntimeView()
})
</script>

<template>
  <div class="app-runtime-view">
    <div v-if="isLoading" class="runtime-state">
      <ProgressSpinner stroke-width="4" />
      <p>Loading application...</p>
    </div>

    <div v-else-if="error" class="runtime-state">
      <i class="pi pi-exclamation-triangle runtime-state__icon"></i>
      <p>{{ error }}</p>
      <p v-if="failureLine" class="runtime-state__failure" data-testid="runtime-failure">{{ failureLine }}</p>
      <div v-if="isNotDeployed && isCatalogApp" class="runtime-state__source-guidance" data-testid="runtime-catalog-guidance">
        <p v-if="catalogUpdate">{{ updateLabel === 'Retry' ? 'Retry builds this version again.' : 'A newer version is in the catalog.' }} Its data, settings and secrets are kept.</p>
        <p v-else>Open its page in Studio to see why, or check its Activity.</p>
      </div>
      <div v-else-if="isNotDeployed" class="runtime-state__source-guidance">
        <p>Edit and deploy from your local project with Automaton CLI.</p>
        <CopyCommand command="automaton deploy" button-label="Copy automaton deploy" />
      </div>
      <div class="runtime-state__actions">
        <button class="runtime-action" type="button" @click="router.push(`/apps/${appId}`)">Back to Studio</button>
        <button v-if="isNotDeployed && isCatalogApp" class="runtime-action" type="button" data-testid="runtime-log" @click="router.push({ path: `/apps/${appId}`, query: { view: 'activity' } })">View log</button>
        <button v-if="isNotDeployed && isCatalogApp && catalogUpdate" class="runtime-action runtime-action--primary" type="button" data-testid="runtime-update" :disabled="updateBusy" @click="runUpdate">{{ updateLabel }}</button>
        <button v-else class="runtime-action runtime-action--primary" type="button" @click="loadRuntimeView">Retry</button>
      </div>
    </div>

    <div v-else-if="!canBeFramed" class="runtime-state" data-testid="runtime-not-framable">
      <i class="pi pi-external-link runtime-state__icon"></i>
      <p>This app cannot be shown inside Studio. Open it in its own tab instead.</p>
      <div class="runtime-state__actions">
        <button class="runtime-action" type="button" @click="router.push(`/apps/${appId}`)">Back to Studio</button>
        <button class="runtime-action runtime-action--primary" type="button" data-testid="open-in-new-tab" @click="openInNewTab">Open in a new tab</button>
      </div>
    </div>

    <template v-else>
      <iframe
        :key="applicationUrl"
        :src="applicationUrl"
        class="runtime-iframe"
        sandbox="allow-scripts allow-same-origin allow-forms allow-modals allow-downloads"
        title="Running Application"
      />
    </template>
  </div>
</template>

<style scoped>
.app-runtime-view {
  display: flex;
  flex-direction: column;
  min-height: 100vh;
  background: var(--p-surface-ground);
}

.runtime-state__actions {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}

.runtime-state__failure { margin: 0; max-width: 40rem; color: var(--p-text-muted-color); overflow-wrap: anywhere; }
.runtime-state__actions { flex-wrap: wrap; justify-content: center; }
.runtime-state__source-guidance p {
  margin: 0 0 0.75rem;
  color: var(--p-text-muted-color);
}

.runtime-iframe {
  flex: 1;
  width: 100%;
  min-height: 0;
  border: 0;
  background: white;
}

.runtime-state {
  flex: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 1rem;
  padding: 2rem;
  text-align: center;
}

.runtime-state__icon {
  font-size: 2rem;
  color: var(--p-orange-500);
}

.runtime-action {
  appearance: none;
  border: 1px solid var(--line);
  background: transparent;
  color: var(--ink);
  border-radius: 0.5rem;
  padding: 0.625rem 0.9rem;
  cursor: pointer;
}

.runtime-action--primary {
  background: var(--p-primary-500);
  border-color: var(--p-primary-500);
  color: white;
}
</style>
