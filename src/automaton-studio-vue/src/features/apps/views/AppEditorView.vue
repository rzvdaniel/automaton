<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, ref, watch } from 'vue'
import { useRoute, useRouter, type LocationQueryRaw } from 'vue-router'
import Button from 'primevue/button'
import ProgressSpinner from 'primevue/progressspinner'
import Tag from 'primevue/tag'
import { useConfirm } from 'primevue/useconfirm'
import { useToast } from 'primevue/usetoast'
import ActionSchedulingPanel from '../components/ActionSchedulingPanel.vue'
import AppDeploymentTargets from '../components/AppDeploymentTargets.vue'
import AppEditorSideNav from '../components/AppEditorSideNav.vue'
import AppOperationsOverview from '../components/AppOperationsOverview.vue'
import AppPublishingView from '../components/AppPublishingView.vue'
import AppRuntimeOperations from '../components/AppRuntimeOperations.vue'
import AppRuntimeLogs from '../components/AppRuntimeLogs.vue'
import AppSettings from '../components/AppSettings.vue'
import PublishDialog from '../components/PublishDialog.vue'
import {
  APP_NAVIGATION_GROUPS,
  resolveAppView,
  withoutAuthoringQuery,
  type AppViewKey,
} from '../app-navigation'
import { appsService } from '../services/apps.service'
import { backendRuntimeLogClient } from '../services/backend-runtime-log-client'
import { useAppEditorStore } from '../stores/app-editor.store'
import type { AppDeploymentTarget, PublishSettingsRequest } from '../types/apps.types'
import type { RuntimeLogConnectionState } from '../types/console.types'
import { getLiveTarget } from '../utils/deployment-target-actions'
import { getEnvironmentDisplayName } from '../utils/environment-display'
import { getPublishedAppUrl } from '../utils/public-app-url'
import { getPublishingConsistencyIssue } from '../utils/publishing-state'
import { resolveViewingTarget } from '../utils/viewing-target'
import { useSidebarState } from '@/shared/composables/useSidebarState'
import { tokenStorage } from '@/shared/services/api/axios-instance'

const route = useRoute()
const router = useRouter()
const confirm = useConfirm()
const toast = useToast()
const editorStore = useAppEditorStore()
const { closeMobile, setHovered } = useSidebarState()

const appId = computed(() => String(route.params.id ?? ''))
const publishingStateLabel = computed(() => editorStore.currentApp && getPublishingConsistencyIssue(editorStore.currentApp)
  ? 'Needs review'
  : editorStore.isPublished ? 'Published' : 'Unpublished')
const activeView = ref<AppViewKey>(resolveAppView(route.query.view))
const deploymentTargets = ref<AppDeploymentTarget[]>([])
const selectedViewingTarget = ref<AppDeploymentTarget | null>(null)
const selectedLogTargetId = ref<string | null>(null)
const runtimeLogConnectionState = ref<RuntimeLogConnectionState>(backendRuntimeLogClient.connectionState)
const isRuntimeLogSubscribed = ref(false)
const runtimeLogSubscriptionFailed = ref(false)
const targetsRevision = ref(0)
const showPublishDialog = ref(false)
const runtimeLogsRef = ref<{ loadLogs: () => void } | null>(null)
let unsubscribeBackendRuntimeLogs: (() => void) | null = null
let unsubscribeBackendRuntimeLogState: (() => void) | null = null

watch(activeView, (view) => {
  if (view === 'logs') void nextTick(() => runtimeLogsRef.value?.loadLogs())
})

watch(() => route.query.target, () => {
  if (deploymentTargets.value.length > 0) void selectViewingTargetFromRoute()
})

onMounted(async () => {
  closeMobile()
  setHovered(false)
  await router.replace({ query: withoutAuthoringQuery(route.query) })

  unsubscribeBackendRuntimeLogs = backendRuntimeLogClient.onLog((event) => {
    if (event.appId !== appId.value) return
    if (selectedLogTargetId.value && event.deploymentTargetId !== selectedLogTargetId.value) return
    editorStore.addBackendRuntimeLogEntry(event)
  })
  unsubscribeBackendRuntimeLogState = backendRuntimeLogClient.onConnectionStateChange((state) => {
    runtimeLogConnectionState.value = state
  })

  window.addEventListener('message', handleApplicationMessage)

  try {
    await editorStore.loadApp(appId.value)
    handleTargetsChanged(await appsService.listDeploymentTargets(appId.value))
  } catch (error: unknown) {
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: error instanceof Error ? error.message : 'Failed to load app',
      life: 5000,
    })
    await router.push('/apps')
  }

  document.querySelector('.app-main')?.classList.add('app-main--fullscreen')
})

onUnmounted(() => {
  window.removeEventListener('message', handleApplicationMessage)
  document.querySelector('.app-main')?.classList.remove('app-main--fullscreen')
  void backendRuntimeLogClient.unsubscribe(appId.value)
  unsubscribeBackendRuntimeLogs?.()
  unsubscribeBackendRuntimeLogState?.()
  editorStore.reset()
})

function handleApplicationMessage(event: MessageEvent) {
  if (event.data?.type === 'requestToken') void handleTokenRequest(event)
}

async function handleTokenRequest(event: MessageEvent) {
  const { requestId } = event.data
  let token = tokenStorage.getAccessToken() ?? ''

  if (isTokenExpired(token)) {
    try {
      const response = await fetch(`${import.meta.env.VITE_API_BASE_URL}/api/Token/RefreshAccessToken`, {
        method: 'POST',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({}),
      })
      if (response.ok) {
        const data = await response.json()
        tokenStorage.setAccessToken(data.accessToken)
        token = data.accessToken
      }
    } catch {
      // Keep using the current token if refresh fails.
    }
  }

  ;(event.source as WindowProxy)?.postMessage({ type: 'tokenResponse', requestId, token }, '*')
}

function isTokenExpired(token: string): boolean {
  if (!token) return true
  try {
    const encodedPayload = token.split('.')[1]
    if (!encodedPayload) return true
    const payload = JSON.parse(atob(encodedPayload)) as { exp?: number }
    return typeof payload.exp !== 'number' || payload.exp * 1000 < Date.now() + 30_000
  } catch {
    return true
  }
}

function handleNavigation(view: AppViewKey) {
  activeView.value = view
  const query: LocationQueryRaw = { ...route.query }
  delete query.settings
  if (view === 'overview') delete query.view
  else query.view = view
  void router.replace({ query })
}

function handleTargetsChanged(targets: AppDeploymentTarget[]) {
  deploymentTargets.value = targets
  const liveTarget = getLiveTarget(targets)
  editorStore.setDefaultTarget(liveTarget)
  void selectViewingTargetFromRoute()
}

async function selectViewingTargetFromRoute() {
  const requestedSlug = typeof route.query.target === 'string' ? route.query.target : ''
  const { target, usedFallback } = resolveViewingTarget(deploymentTargets.value, requestedSlug)
  if (target) await selectViewingTarget(target, usedFallback)
}

async function selectViewingTarget(target: AppDeploymentTarget, replaceRoute = false) {
  selectedViewingTarget.value = target
  selectedLogTargetId.value = target.id
  isRuntimeLogSubscribed.value = false
  runtimeLogSubscriptionFailed.value = false

  if (replaceRoute || route.query.target !== target.slug) {
    await router.replace({ query: { ...route.query, target: target.slug } })
  }

  try {
    if (target.runner) {
      await backendRuntimeLogClient.subscribe(target.appId, target.id)
      isRuntimeLogSubscribed.value = backendRuntimeLogClient.isSubscribedTo(target.appId, target.id)
    } else {
      await backendRuntimeLogClient.unsubscribe(target.appId)
    }
  } catch {
    runtimeLogSubscriptionFailed.value = true
    toast.add({
      severity: 'warn',
      summary: 'Live logs unavailable',
      detail: 'The runtime selection changed, but log streaming could not be updated.',
      life: 4000,
    })
  }
}

function handleSelectViewingTarget(target: AppDeploymentTarget) {
  void selectViewingTarget(target)
}

async function handleViewTargetLogs(target: AppDeploymentTarget) {
  await selectViewingTarget(target)
  activeView.value = 'logs'
}

async function handlePublish(request: PublishSettingsRequest) {
  try {
    const app = editorStore.currentApp
    if (!app) return
    await editorStore.publishApp({
      ...request,
      category: app.category ?? null,
      tags: app.tags ?? null,
    })
    showPublishDialog.value = false
    toast.add({
      severity: 'success',
      summary: 'Published',
      detail: request.runtimeVisibility === 'public'
        ? `Public access configured at ${getPublishedAppUrl(request.publicUrl)}. Publishing does not deploy or update the Live runtime.`
        : 'Publishing settings saved. The runtime remains private.',
      life: 5000,
    })
  } catch (error: unknown) {
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: error instanceof Error ? error.message : 'Failed to publish app',
      life: 5000,
    })
  }
}

function handleUnpublish() {
  confirm.require({
    message: 'Unpublish this app? Public access and its marketplace listing will be removed. The Live deployment and runtime will remain in place.',
    header: 'Confirm unpublish',
    icon: 'pi pi-exclamation-triangle',
    acceptLabel: 'Unpublish',
    rejectLabel: 'Cancel',
    accept: async () => {
      try {
        await editorStore.unpublishApp()
        toast.add({ severity: 'success', summary: 'Unpublished', detail: 'Public access was removed. The Live deployment and runtime were not changed.', life: 4000 })
      } catch (error: unknown) {
        toast.add({
          severity: 'error',
          summary: 'Error',
          detail: error instanceof Error ? error.message : 'Failed to unpublish app',
          life: 5000,
        })
      }
    },
  })
}
</script>

<template>
  <div class="app-operations-view">
    <div v-if="editorStore.isLoading" class="state-container">
      <ProgressSpinner />
      <p>Loading app...</p>
    </div>

    <template v-else-if="editorStore.currentApp">
      <header class="operations-header">
        <div class="app-identity">
          <Button icon="pi pi-arrow-left" aria-label="Back to apps" text rounded @click="router.push('/apps')" />
          <div>
            <h1>{{ editorStore.currentApp.name }}</h1>
            <div class="header-status">
              <Tag :value="editorStore.applicationState.label" :severity="editorStore.applicationState.severity" />
              <Tag :value="publishingStateLabel" :severity="publishingStateLabel === 'Needs review' ? 'warn' : editorStore.isPublished ? 'success' : 'secondary'" />
            </div>
          </div>
        </div>
      </header>

      <div class="operations-shell">
        <AppEditorSideNav
          :active-item="activeView"
          :groups="APP_NAVIGATION_GROUPS"
          @select="handleNavigation"
        />

        <main class="operations-content">
          <AppOperationsOverview
            v-if="activeView === 'overview'"
            :app="editorStore.currentApp"
            :targets="deploymentTargets"
            :application-state="editorStore.applicationState"
            @navigate="handleNavigation"
          />

          <AppPublishingView
            v-else-if="activeView === 'publishing'"
            :app="editorStore.currentApp"
            :has-live-target="!!getLiveTarget(deploymentTargets)"
            :operational-state="editorStore.applicationState"
            @publish="showPublishDialog = true"
            @unpublish="handleUnpublish"
            @navigate="activeView = $event"
          />

          <AppDeploymentTargets
            v-else-if="activeView === 'deployments'"
            :app-id="appId"
            :project-id="editorStore.currentApp.projectId"
            :refresh-revision="targetsRevision"
            :show-deploy-action="false"
            @view-logs="handleViewTargetLogs"
            @targets-changed="handleTargetsChanged"
          />

          <AppRuntimeOperations
            v-else-if="activeView === 'runtime'"
            :app="editorStore.currentApp"
            :targets="deploymentTargets"
            :selected-target="selectedViewingTarget"
            @select-target="handleSelectViewingTarget"
            @navigate="handleNavigation"
          />

          <AppRuntimeLogs
            v-else-if="activeView === 'logs'"
            ref="runtimeLogsRef"
            :app-id="appId"
            :app-name="editorStore.currentApp.name"
            :targets="deploymentTargets"
            :selected-target="selectedViewingTarget"
            :live-logs="editorStore.consoleEntries"
            :connection-state="runtimeLogConnectionState"
            :is-stream-subscribed="isRuntimeLogSubscribed"
            :stream-error="runtimeLogSubscriptionFailed"
            @select-target="handleSelectViewingTarget"
            @navigate="handleNavigation"
          />

          <ActionSchedulingPanel
            v-else-if="activeView === 'actions'"
            :key="appId"
            :app-id="appId"
          />

          <AppSettings
            v-else-if="activeView === 'settings'"
            :key="activeView"
            :targets-revision="targetsRevision"
            @view-target-logs="handleViewTargetLogs"
            @targets-changed="handleTargetsChanged"
          />
        </main>
      </div>

      <PublishDialog
        v-model:visible="showPublishDialog"
        :current-slug="editorStore.currentApp.publicUrl"
        :is-published="editorStore.currentApp.isPublished"
        :requires-auth="editorStore.currentApp.requiresAuthentication"
        :is-forkable="editorStore.currentApp.isForkable"
        :current-runtime-visibility="editorStore.currentApp.runtimeVisibility"
        :current-catalog-visibility="editorStore.currentApp.catalogVisibility"
        :app-name="editorStore.currentApp.name"
        :has-live-target="!!getLiveTarget(deploymentTargets)"
        :live-target-name="getLiveTarget(deploymentTargets) ? getEnvironmentDisplayName(getLiveTarget(deploymentTargets)) : undefined"
        :live-target-slug="getLiveTarget(deploymentTargets)?.slug"
        :viewing-target-name="selectedViewingTarget ? getEnvironmentDisplayName(selectedViewingTarget) : undefined"
        :viewing-target-slug="selectedViewingTarget?.slug"
        @publish="handlePublish"
      />
    </template>

    <div v-else class="state-container">
      <i class="pi pi-exclamation-triangle"></i>
      <p>{{ editorStore.error || 'App not found' }}</p>
      <Button label="Back to apps" @click="router.push('/apps')" />
    </div>
  </div>
</template>

<style scoped>
.app-operations-view {
  display: flex;
  flex-direction: column;
  height: calc(100vh - 64px);
  overflow: hidden;
  background: var(--p-surface-ground);
}

.operations-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  padding: 0.8rem 1rem;
  border-bottom: 1px solid var(--p-surface-border);
  background: var(--p-surface-card);
}

.app-identity,
.header-actions,
.header-status {
  display: flex;
  align-items: center;
  gap: 0.65rem;
}

.app-identity h1 {
  margin: 0 0 0.35rem;
  font-size: 1.15rem;
}

.operations-shell {
  display: flex;
  flex: 1;
  min-height: 0;
}

.operations-content {
  flex: 1;
  min-width: 0;
  overflow: hidden;
}

.operations-content > :deep(*) {
  height: 100%;
}

.state-container {
  display: flex;
  align-items: center;
  justify-content: center;
  flex-direction: column;
  gap: 1rem;
  height: 100%;
}

@media (max-width: 960px) {
  .operations-shell {
    flex-direction: column;
  }
}

@media (max-width: 760px) {
  .operations-header {
    align-items: stretch;
    flex-direction: column;
  }

  .header-actions {
    flex-wrap: wrap;
  }
}
</style>
