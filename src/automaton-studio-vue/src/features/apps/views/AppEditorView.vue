<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { useRoute, useRouter, type LocationQueryRaw } from 'vue-router'
import Button from 'primevue/button'
import Menu from 'primevue/menu'
import ProgressSpinner from 'primevue/progressspinner'
import { useToast } from 'primevue/usetoast'
import AppActivity from '../components/AppActivity.vue'
import AppMark from '../components/AppMark.vue'
import AppOverview from '../components/AppOverview.vue'
import AppSettingsPage from '../components/AppSettingsPage.vue'
import ShareDialog from '../components/ShareDialog.vue'
import {
  APP_TABS,
  opensAdvanced,
  resolveAppViewFromQuery,
  withoutAuthoringQuery,
  type AppViewKey,
} from '../app-navigation'
import { actionSchedulesService, type ActionModule, type ActionSchedule } from '../services/action-schedules.service'
import { appsService, type AppActionExecution } from '../services/apps.service'
import { backendRuntimeLogClient } from '../services/backend-runtime-log-client'
import { useAppEditorStore } from '../stores/app-editor.store'
import type { AppDeploymentTarget } from '../types/apps.types'
import { getLiveTarget } from '../utils/deployment-target-actions'
import { getDeployedAppUrl, getPublishedAppUrl, getStudioApplicationUrl } from '../utils/public-app-url'
import { appKindOf, getAppHealth, isActiveRun } from '../utils/app-overview'
import { tokenStorage } from '@/shared/services/api/axios-instance'

const route = useRoute()
const router = useRouter()
const toast = useToast()
const editorStore = useAppEditorStore()

const appId = computed(() => String(route.params.id ?? ''))
const activeView = ref<AppViewKey>(resolveAppViewFromQuery(route.query))
const advancedOpen = ref(opensAdvanced(route.query))
const deploymentTargets = ref<AppDeploymentTarget[]>([])
const executions = ref<AppActionExecution[]>([])
const schedules = ref<ActionSchedule[]>([])
const actions = ref<ActionModule[]>([])
const showShareDialog = ref(false)
const actionMenu = ref<InstanceType<typeof Menu> | null>(null)
const starting = ref(false)
const activityRefresh = ref(0)
let poll: ReturnType<typeof setInterval> | null = null
let unsubscribeBackendRuntimeLogs: (() => void) | null = null

const app = computed(() => editorStore.currentApp)
const kind = computed(() => (app.value ? appKindOf(app.value) : 'web'))
const liveTarget = computed(() => getLiveTarget(deploymentTargets.value))
const lastRun = computed(() => [...executions.value].sort((a, b) => b.createdAtUtc.localeCompare(a.createdAtUtc))[0] ?? null)
const health = computed(() => getAppHealth(editorStore.applicationState, lastRun.value))

const publicAppUrl = computed(() => {
  const current = app.value
  return current?.isPublished && current.canLaunchPublicly && current.publicUrl ? getPublishedAppUrl(current.publicUrl) : ''
})
const openAppUrl = computed(() => {
  if (!app.value || kind.value === 'tool') return ''
  if (publicAppUrl.value) return publicAppUrl.value
  return liveTarget.value?.genericRuntimeDeployed ? getStudioApplicationUrl(app.value.id) : ''
})
/** The address of the app itself (an API's base URL); empty until it is live. */
const address = computed(() => {
  if (!app.value || kind.value === 'tool') return ''
  return publicAppUrl.value || (liveTarget.value?.genericRuntimeDeployed ? getDeployedAppUrl(app.value.id) : '')
})
const apiReferenceUrl = computed(() => {
  const path = app.value?.application?.openapi
  return address.value && path ? new URL(path, address.value).toString() : ''
})
const runMenu = computed(() => actions.value.map((action) => ({
  label: action.name || action.key,
  command: () => void runNow(action.key),
})))
const liveOutputAvailable = computed(() => kind.value !== 'tool' && editorStore.applicationState.status === 'running')
const runtimeLogs = computed(() => editorStore.consoleEntries.filter((entry) =>
  entry.type === 'runtime-log' && entry.deploymentTargetId === liveTarget.value?.id))

function openApp() {
  if (openAppUrl.value) window.open(openAppUrl.value, '_blank', 'noopener')
}

async function copyAddress() {
  try {
    await navigator.clipboard.writeText(address.value)
    toast.add({ severity: 'success', summary: 'Address copied', life: 2000 })
  } catch {
    toast.add({ severity: 'warn', summary: 'Copy this address', detail: address.value, life: 6000 })
  }
}

onMounted(async () => {
  // ?share=1 (and the old ?view=publishing) open the Share dialog.
  const wantsShare = route.query.share === '1' || route.query.view === 'publishing'
  await router.replace({ query: withoutAuthoringQuery(route.query) })

  unsubscribeBackendRuntimeLogs = backendRuntimeLogClient.onLog((event) => {
    if (event.appId !== appId.value) return
    if (liveTarget.value && event.deploymentTargetId !== liveTarget.value.id) return
    editorStore.addBackendRuntimeLogEntry(event)
  })

  window.addEventListener('message', handleApplicationMessage)

  try {
    await editorStore.loadApp(appId.value)
    await handleTargetsChanged(await appsService.listDeploymentTargets(appId.value))
    void loadWhatItDoes()
    if (wantsShare) showShareDialog.value = true
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
  if (poll) clearInterval(poll)
  void backendRuntimeLogClient.unsubscribe(appId.value)
  unsubscribeBackendRuntimeLogs?.()
  editorStore.reset()
})

watch(executions, (runs) => {
  const active = runs.some((run) => isActiveRun(run.status))
  if (active && !poll) poll = setInterval(() => void refreshExecutions(), 3000)
  if (!active && poll) {
    clearInterval(poll)
    poll = null
  }
})

async function refreshExecutions() {
  try {
    executions.value = await appsService.listActionExecutions(appId.value)
    activityRefresh.value++
  } catch {
    // Keep the list we have; the next poll tries again.
  }
}

/** Actions, their schedules and runs: what Overview and the header button need. */
async function loadWhatItDoes() {
  if (!app.value) return
  const id = app.value.id
  const [runs, list, allSchedules] = await Promise.allSettled([
    appsService.listActionExecutions(id),
    kind.value === 'site' ? Promise.resolve([] as ActionModule[]) : actionSchedulesService.listActions(id),
    actionSchedulesService.listSchedules(),
  ])
  if (app.value?.id !== id) return
  executions.value = runs.status === 'fulfilled' ? runs.value : []
  actions.value = list.status === 'fulfilled' ? list.value : []
  schedules.value = allSchedules.status === 'fulfilled' ? allSchedules.value.filter((item) => item.applicationId === id) : []
}

async function runNow(actionKey?: string) {
  const key = actionKey ?? actions.value[0]?.key
  if (!key) return
  starting.value = true
  try {
    await appsService.runAction(appId.value, key, {})
    toast.add({ severity: 'success', summary: `Started ${key}`, life: 2500 })
    await refreshExecutions()
    handleNavigation('activity')
  } catch (error) {
    toast.add({ severity: 'error', summary: 'It could not be started', detail: error instanceof Error ? error.message : undefined, life: 5000 })
  } finally {
    starting.value = false
  }
}

function toggleRunMenu(event: Event) {
  if (actions.value.length > 1) actionMenu.value?.toggle(event)
  else void runNow()
}

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

async function refreshTargets() {
  await handleTargetsChanged(await appsService.listDeploymentTargets(appId.value))
}

async function handleTargetsChanged(targets: AppDeploymentTarget[]) {
  deploymentTargets.value = targets
  const live = getLiveTarget(targets)
  editorStore.setDefaultTarget(live)
  if (!live?.runner) {
    await backendRuntimeLogClient.unsubscribe(appId.value).catch(() => undefined)
    return
  }
  try {
    await backendRuntimeLogClient.subscribe(live.appId, live.id)
  } catch {
    // Live output is optional; the rest of the page works without it.
  }
}

async function handleSharingChanged() {
  // The address people open the app at follows who can open the app.
  await editorStore.refreshApp(appId.value)
}
</script>

<template>
  <div class="app-page">
    <div v-if="editorStore.isLoading" class="state-container">
      <ProgressSpinner />
      <p>Loading app...</p>
    </div>

    <template v-else-if="app">
      <main class="page">
        <header class="head">
          <Button icon="pi pi-arrow-left" aria-label="Back to apps" text rounded @click="router.push('/apps')" />
          <AppMark :id="app.id" :name="app.name" :image-url="app.thumbnailUrl" :size="52" />
          <div class="head-text">
            <div class="title-line">
              <h1>{{ app.name }}</h1>
              <span class="pill" :class="`pill--${health}`" data-testid="app-health">{{ health }}</span>
            </div>
            <p v-if="app.summary" class="summary">{{ app.summary }}</p>
          </div>
          <div class="head-actions">
            <Button label="Share" severity="secondary" outlined data-testid="share-app" @click="showShareDialog = true" />
            <template v-if="kind === 'tool'">
              <Button
                label="Run now"
                icon="pi pi-play"
                :loading="starting"
                :disabled="actions.length === 0"
                :title="actions.length === 0 ? 'No action is available. Deploy the app and bring its Runner online.' : undefined"
                data-testid="run-now"
                @click="toggleRunMenu"
              />
              <Menu ref="actionMenu" :model="runMenu" popup />
            </template>
            <template v-else-if="kind === 'api'">
              <Button v-if="apiReferenceUrl" as="a" :href="apiReferenceUrl" target="_blank" rel="noopener noreferrer" label="API reference" severity="secondary" outlined />
              <Button label="Copy address" icon="pi pi-copy" :disabled="!address" data-testid="copy-address" @click="copyAddress" />
            </template>
            <Button v-else-if="openAppUrl" label="Open app" icon="pi pi-external-link" data-testid="open-app" @click="openApp" />
          </div>
        </header>

        <div class="tabs" role="tablist" aria-label="App sections">
          <button
            v-for="tab in APP_TABS"
            :key="tab.key"
            type="button"
            role="tab"
            class="tab"
            :class="{ on: activeView === tab.key }"
            :aria-selected="activeView === tab.key"
            @click="handleNavigation(tab.key)"
          >{{ tab.label }}</button>
        </div>

        <AppOverview
          v-if="activeView === 'overview'"
          :app="app"
          :targets="deploymentTargets"
          :executions="executions"
          :schedules="schedules"
          :actions="actions"
          :address="address"
          @navigate="handleNavigation"
          @share="showShareDialog = true"
        />
        <AppActivity
          v-else-if="activeView === 'activity'"
          :app-id="app.id"
          :executions="executions"
          :live-output-available="liveOutputAvailable"
          :live-logs="runtimeLogs"
          :refresh-key="activityRefresh"
        />
        <AppSettingsPage
          v-else
          :app="app"
          :targets="deploymentTargets"
          :open-advanced="advancedOpen"
          @targets-changed="refreshTargets"
        />
      </main>

      <ShareDialog
        v-model:visible="showShareDialog"
        :app-id="app.id"
        :app-name="app.name"
        @changed="handleSharingChanged"
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
.app-page {
  height: calc(100vh - 64px);
  overflow-y: auto;
  background: var(--app-bg);
}

.page {
  display: flex;
  flex-direction: column;
  gap: 18px;
  max-width: 1080px;
  margin: 0 auto;
  padding: 28px clamp(16px, 4vw, 48px) 48px;
  box-sizing: border-box;
}

.head {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 16px;
}

.head-text {
  flex: 1 1 260px;
  min-width: 0;
}

.title-line {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 10px;
}

h1 {
  margin: 0;
  font-family: var(--display);
  font-size: 26px;
  font-weight: 700;
}

.summary {
  margin: 4px 0 0;
  color: var(--ink-soft);
}

.head-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.pill {
  padding: 3px 10px;
  border-radius: 999px;
  font-family: var(--mono);
  font-size: 12px;
}

.pill--healthy { border: 1px solid var(--ok); color: var(--ok); }
.pill--failing { background: color-mix(in srgb, var(--signal) 18%, transparent); color: var(--signal-ink); }
.pill--stopped { background: var(--row-line); color: var(--ink-soft); }

.tabs {
  display: flex;
  gap: 4px;
  border-bottom: 1px solid var(--line);
}

.tab {
  min-height: 40px;
  padding: 0 14px;
  border: 0;
  border-bottom: 2px solid transparent;
  background: transparent;
  color: var(--ink-soft);
  font: inherit;
  cursor: pointer;
}

.tab.on {
  border-bottom-color: var(--signal);
  color: var(--ink);
  font-weight: 600;
}

.state-container {
  display: flex;
  align-items: center;
  justify-content: center;
  flex-direction: column;
  gap: 1rem;
  height: 100%;
}
</style>
