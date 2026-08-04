<script setup lang="ts">
import Splitter from 'primevue/splitter'
import SplitterPanel from 'primevue/splitterpanel'
import { useToast } from 'primevue/usetoast'
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { useAppEditorStore } from '../stores/app-editor.store'
import { DeploymentTargetStatus, type AppDeploymentTarget, APPLICATION_VIEWPORT_SIZES } from '../types/apps.types'
import { useUserSettings } from '@/shared/composables/useUserSettings'
import ConsolePanel from './ConsolePanel.vue'
import { getDeploymentTargetUrl } from '../utils/public-app-url'
import { getViewingTargetPreviewState } from '../utils/viewing-target'

const props = withDefaults(
  defineProps<{
    suspendHeavyRendering?: boolean
    runnerId?: string | null
    targets?: AppDeploymentTarget[]
    selectedTarget?: AppDeploymentTarget | null
  }>(),
  {
    suspendHeavyRendering: false,
    runnerId: null,
    targets: () => [],
    selectedTarget: null,
  },
)

const emit = defineEmits<{
  selectTarget: [target: AppDeploymentTarget]
}>()

const editorStore = useAppEditorStore()
const router = useRouter()
const toast = useToast()
const { isDark } = useUserSettings()

const currentSize = computed(() => APPLICATION_VIEWPORT_SIZES[editorStore.applicationViewportMode] ?? APPLICATION_VIEWPORT_SIZES.desktop)
const SPLITTER_STORAGE_KEY = 'app-editor-build-visual-splitter'
const defaultSplitterSizes = [84, 16]

function normalizeSizes(sizes: number[]): number[] {
  if (!Array.isArray(sizes) || sizes.length !== 2) {
    return defaultSplitterSizes
  }

  const total = sizes.reduce((sum, size) => sum + size, 0)
  if (total <= 0) {
    return defaultSplitterSizes
  }

  return sizes.map((size) => Math.round((size / total) * 10000) / 100)
}

function loadSplitterSizes(): number[] {
  try {
    const raw = localStorage.getItem(SPLITTER_STORAGE_KEY)
    if (!raw) {
      return defaultSplitterSizes
    }

    const parsed = JSON.parse(raw)
    if (Array.isArray(parsed) && parsed.every((value) => typeof value === 'number')) {
      return normalizeSizes(parsed as number[])
    }
  } catch {
    // fallback to defaults
  }

  return defaultSplitterSizes
}

function saveSplitterSizes(sizes: number[]): void {
  try {
    localStorage.setItem(SPLITTER_STORAGE_KEY, JSON.stringify(sizes))
  } catch {
    // non-critical persistence error
  }
}

const splitterSizes = ref<number[]>(loadSplitterSizes())
const splitterRenderKey = ref(0)
const isLocalResizing = ref(false)

function handleSplitterResize(event: { sizes?: number[] }): void {
  if (!event.sizes || !Array.isArray(event.sizes)) {
    return
  }

  const normalized = normalizeSizes(event.sizes)
  splitterSizes.value = normalized
  saveSplitterSizes(normalized)
  isLocalResizing.value = false
}

if (splitterSizes.value.length === 2) {
  saveSplitterSizes(splitterSizes.value)
}

function handleSplitterResizing(): void {
  isLocalResizing.value = true
}

const isHeavyRenderingSuspended = computed(() => props.suspendHeavyRendering || isLocalResizing.value)
const isAdvancedExpanded = ref(true)

// Backend connection status
const backendConnected = computed(() => !!props.runnerId)
const showBackendPrompt = computed(
  () => editorStore.hasBackendFolder && !backendConnected.value,
)

// Card dismissed by user — re-opens if they click the badge
const backendCardDismissed = ref(false)
const showBackendCard = computed(() => showBackendPrompt.value && !backendCardDismissed.value)

// Idle hint: show after 12s if preview is loaded and backend is needed
const showIdleHint = ref(false)
let idleTimer: ReturnType<typeof setTimeout> | null = null

function startIdleTimer() {
  if (idleTimer) clearTimeout(idleTimer)
  idleTimer = setTimeout(() => {
    if (showBackendPrompt.value) showIdleHint.value = true
  }, 12000)
}

watch(
  () => props.selectedTarget?.id,
  (targetId) => {
    if (targetId && showBackendPrompt.value) startIdleTimer()
  },
)

onMounted(() => {
  if (props.selectedTarget && showBackendPrompt.value) startIdleTimer()
})

onUnmounted(() => {
  if (idleTimer) clearTimeout(idleTimer)
})

const previewViewportStyle = computed(() => {
  if (editorStore.applicationViewportMode === 'desktop') {
    return {
      width: '100%',
      height: '100%',
      maxWidth: currentSize.value.width,
      maxHeight: '100%',
    }
  }

  return {
    width: currentSize.value.width,
    height: currentSize.value.height,
    maxWidth: '100%',
    maxHeight: '100%',
  }
})

const previewState = computed(() => getViewingTargetPreviewState(props.selectedTarget))

const targetPreviewUrl = computed(() => {
  if (!previewState.value.available || !editorStore.currentApp || !props.selectedTarget) return ''

  return getDeploymentTargetUrl(editorStore.currentApp.id, props.selectedTarget.routePath)
})

const previewRefreshKey = ref(Date.now())
const iframePreviewUrl = computed(() => {
  if (!targetPreviewUrl.value) return ''
  const runtimeUrl = new URL(targetPreviewUrl.value, window.location.origin)
  runtimeUrl.searchParams.set('v', previewRefreshKey.value.toString())
  return runtimeUrl.toString()
})

const canOpenApplication = computed(() => !!targetPreviewUrl.value)

function getTargetDestinationLabel(target?: AppDeploymentTarget | null): string {
  if (!target?.runner) return 'No runtime assigned'
  if (target.runner.isHostedRunner) return 'Automaton Hosted'
  return target.runner.applicationProfile === 'PrivilegedHost'
    ? 'Self-hosted · Privileged Host'
    : 'Self-hosted · Standard'
}

function getTargetStatusLabel(target: AppDeploymentTarget): string {
  if (!target.runner) return 'No runtime assigned'
  if (!target.frontendDeployed) return 'Not deployed'
  if (!target.runner.isOnline || target.status === DeploymentTargetStatus.RunnerOffline) return 'Offline'
  return target.status === DeploymentTargetStatus.Running ? 'Running' : target.status
}

function selectTarget(event: Event): void {
  const target = props.targets.find((candidate) => candidate.slug === (event.target as HTMLSelectElement).value)
  if (target) emit('selectTarget', target)
}

function openApplicationInNewTab(): void {
  const url = targetPreviewUrl.value
  if (!url) return
  window.open(url, '_blank', 'noopener')
}

async function copyApplicationLink(): Promise<void> {
  const url = targetPreviewUrl.value
  if (!url) return

  try {
    await navigator.clipboard.writeText(url)
    toast.add({
      severity: 'success',
      summary: 'Copied',
      detail: 'Target preview URL copied to clipboard',
      life: 2500,
    })
  } catch {
    toast.add({
      severity: 'error',
      summary: 'Copy failed',
      detail: 'Unable to copy the application URL',
      life: 3000,
    })
  }
}

async function refreshApplication(): Promise<void> {
  previewRefreshKey.value = Date.now()
}
</script>

<template>
  <div class="build-visual-workspace">
    <div class="application-shell-header">
      <div class="application-shell-header__title">
        <div>
          <span class="application-shell-header__label">Application preview</span>
          <div class="application-target-picker">
            <span>Viewing target:</span>
            <select
              v-if="targets.length > 1"
              class="application-target-select"
              :value="selectedTarget?.slug"
              aria-label="Viewing target"
              @change="selectTarget"
            >
              <option v-for="target in targets" :key="target.id" :value="target.slug">
                {{ target.name }}{{ target.isDefault ? ' — Live' : '' }} · {{ getTargetDestinationLabel(target) }} · {{ getTargetStatusLabel(target) }}
              </option>
            </select>
            <span v-else class="application-target-summary">
              {{ selectedTarget?.name || 'Live' }} · {{ getTargetDestinationLabel(selectedTarget) }} · {{ selectedTarget ? getTargetStatusLabel(selectedTarget) : '' }}
            </span>
          </div>
        </div>
      </div>
      <div class="application-shell-header__actions">
        <button
          class="application-shell-action"
          type="button"
          :disabled="!canOpenApplication"
          @click="openApplicationInNewTab"
        >
          <i class="pi pi-external-link"></i>
          <span>Open preview</span>
        </button>
        <button
          class="application-shell-action"
          type="button"
          :disabled="!canOpenApplication"
          @click="copyApplicationLink"
        >
          <i class="pi pi-copy"></i>
          <span>Copy preview link</span>
        </button>
        <button
          class="application-shell-action"
          type="button"
          :disabled="editorStore.isLoadingApplication"
          @click="refreshApplication"
        >
          <i class="pi pi-refresh"></i>
          <span>Refresh</span>
        </button>
      </div>
    </div>

    <!-- Collapsed: application fills all space -->
    <div v-if="!isAdvancedExpanded" class="preview-only">
      <div class="preview-content">
        <div
          class="application-viewport"
          :class="editorStore.applicationViewportMode"
          :style="previewViewportStyle"
        >
          <div v-if="isHeavyRenderingSuspended" class="application-loading">
            <iframe
              v-if="iframePreviewUrl"
              :key="iframePreviewUrl"
              :src="iframePreviewUrl"
              class="application-iframe"
              sandbox="allow-scripts allow-same-origin allow-forms allow-modals allow-downloads"
              title="Running Application"
            />
            <div class="preview-resize-overlay">
              <i class="pi pi-arrows-v text-3xl text-color-secondary"></i>
              <p class="mt-2 text-color-secondary">Resizing application...</p>
            </div>
          </div>
          <div v-else-if="editorStore.isLoadingApplication" class="application-loading">
            <i class="pi pi-spin pi-spinner text-3xl"></i>
            <p class="mt-2">Loading application...</p>
          </div>
          <div v-else-if="!iframePreviewUrl" class="application-empty">
            <i class="pi pi-desktop text-3xl text-color-secondary"></i>
            <p class="mt-2 text-color-secondary">{{ previewState.message }}</p>
          </div>
          <iframe
              v-else
              :key="iframePreviewUrl"
              :src="iframePreviewUrl"
              class="application-iframe"
              :class="{ 'application-iframe--no-pointer': isHeavyRenderingSuspended }"
              sandbox="allow-scripts allow-same-origin allow-forms allow-modals allow-downloads"
              title="Running Application"
            />
            <!-- Inline backend prompt (non-blocking overlay at bottom of iframe) -->
            <div v-if="showBackendCard" class="backend-prompt">
              <div class="backend-prompt-card">
                <div class="backend-prompt-icon">⚡</div>
                <div class="backend-prompt-body">
                  <p class="backend-prompt-title">Backend execution requires Pro</p>
                  <p class="backend-prompt-desc">The running application can render without backend execution. Live logic, APIs, and workflows require a Pro plan or a self-hosted runner.</p>
                  <div class="backend-prompt-actions">
                    <button
                      class="prompt-btn prompt-btn--primary"
                      v-tooltip.top="'Your backend starts in seconds. No setup required.'"
                      @click="router.push('/billing')"
                    >
                      <i class="pi pi-bolt"></i> Upgrade to Pro
                    </button>
                    <button
                      class="prompt-btn prompt-btn--secondary"
                      v-tooltip.top="'Run a lightweight container on your own machine or server. Free, no lock-in.'"
                      @click="router.push('/runners')"
                    >
                      <i class="pi pi-server"></i> Self-host
                    </button>
                  </div>
                </div>
                <button class="backend-prompt-dismiss" v-tooltip.top="'Dismiss'" @click="backendCardDismissed = true">
                  <i class="pi pi-times"></i>
                </button>
              </div>
            </div>
            <!-- Idle hint (appears after ~12s of inactivity) -->
            <div v-if="showIdleHint && !showBackendCard" class="idle-hint">
              <i class="pi pi-info-circle"></i>
              This app includes backend features. Interact with it to see them in action.
              <button class="idle-hint-dismiss" @click="showIdleHint = false">&times;</button>
            </div>
        </div>
      </div>
      <!-- Console toggle bar (collapsed) -->
      <button
        class="advanced-toggle-handle"
        :class="{ 'advanced-toggle-handle-dark': isDark }"
        @click="isAdvancedExpanded = true"
      >
        <i class="pi pi-chevron-up"></i>
        <span>Logs</span>
      </button>
    </div>

    <!-- Expanded: splitter so user can drag -->
    <Splitter
      v-else
      :key="splitterRenderKey"
      class="visual-splitter"
      layout="vertical"
      :gutter-size="8"
      @resize="handleSplitterResizing"
      @resizeend="handleSplitterResize"
    >
      <SplitterPanel :size="splitterSizes[0]" :min-size="30" class="splitter-panel">
        <div class="preview-content">
          <div
            class="application-viewport"
            :class="editorStore.applicationViewportMode"
            :style="previewViewportStyle"
          >
            <div v-if="isHeavyRenderingSuspended" class="application-loading">
              <iframe
                v-if="iframePreviewUrl"
                :key="iframePreviewUrl"
                :src="iframePreviewUrl"
                class="application-iframe"
                sandbox="allow-scripts allow-same-origin allow-forms allow-modals allow-downloads"
                title="Running Application"
              />
              <div class="preview-resize-overlay">
                <i class="pi pi-arrows-v text-3xl text-color-secondary"></i>
                <p class="mt-2 text-color-secondary">Resizing application...</p>
              </div>
            </div>
            <div v-else-if="editorStore.isLoadingApplication" class="application-loading">
              <i class="pi pi-spin pi-spinner text-3xl"></i>
              <p class="mt-2">Loading application...</p>
            </div>
            <div v-else-if="!iframePreviewUrl" class="application-empty">
            <i class="pi pi-desktop text-3xl text-color-secondary"></i>
            <p class="mt-2 text-color-secondary">{{ previewState.message }}</p>
            </div>
            <iframe
              v-else
              :key="iframePreviewUrl"
              :src="iframePreviewUrl"
              class="application-iframe"
              :class="{ 'application-iframe--no-pointer': isHeavyRenderingSuspended }"
              sandbox="allow-scripts allow-same-origin allow-forms allow-modals allow-downloads"
              title="Running Application"
            />
            <!-- Inline backend prompt (non-blocking overlay at bottom of iframe) -->
            <div v-if="showBackendCard" class="backend-prompt">
              <div class="backend-prompt-card">
                <div class="backend-prompt-icon">⚡</div>
                <div class="backend-prompt-body">
                  <p class="backend-prompt-title">Backend execution requires Pro</p>
                  <p class="backend-prompt-desc">The running application can render without backend execution. Live logic, APIs, and workflows require a Pro plan or a self-hosted runner.</p>
                  <div class="backend-prompt-actions">
                    <button
                      class="prompt-btn prompt-btn--primary"
                      v-tooltip.top="'Your backend starts in seconds. No setup required.'"
                      @click="router.push('/billing')"
                    >
                      <i class="pi pi-bolt"></i> Upgrade to Pro
                    </button>
                    <button
                      class="prompt-btn prompt-btn--secondary"
                      v-tooltip.top="'Run a lightweight container on your own machine or server. Free, no lock-in.'"
                      @click="router.push('/runners')"
                    >
                      <i class="pi pi-server"></i> Self-host
                    </button>
                  </div>
                </div>
                <button class="backend-prompt-dismiss" v-tooltip.top="'Dismiss'" @click="backendCardDismissed = true">
                  <i class="pi pi-times"></i>
                </button>
              </div>
            </div>
            <!-- Idle hint (appears after ~12s of inactivity) -->
            <div v-if="showIdleHint && !showBackendCard" class="idle-hint">
              <i class="pi pi-info-circle"></i>
              This app includes backend features. Interact with it to see them in action.
              <button class="idle-hint-dismiss" @click="showIdleHint = false">&times;</button>
            </div>
          </div>
        </div>
      </SplitterPanel>

      <SplitterPanel :size="splitterSizes[1]" :min-size="12" class="splitter-panel">
        <!-- Console toggle bar (expanded) -->
        <button
          class="advanced-toggle-handle advanced-toggle-handle-expanded"
          :class="{ 'advanced-toggle-handle-dark': isDark }"
          @click="isAdvancedExpanded = false"
        >
          <i class="pi pi-chevron-down"></i>
          <span>Console</span>
        </button>
        <div v-if="isHeavyRenderingSuspended" class="logs-placeholder">
          <i class="pi pi-arrows-v text-color-secondary"></i>
          <span class="text-sm text-color-secondary">Resizing logs...</span>
        </div>
        <div v-else class="logs-tabbed-container">
          <div class="logs-tab-bar">
            <div class="logs-tab logs-tab--static active">
              <i class="pi pi-server"></i>
              Backend logs · {{ selectedTarget?.name || 'Live' }} — {{ selectedTarget?.slug || 'live' }}
              <span v-if="editorStore.consoleEntries.length > 0" class="logs-tab-badge">{{ editorStore.consoleEntries.length }}</span>
            </div>
          </div>
          <ConsolePanel class="logs-panel" :target-id="selectedTarget?.id" />
        </div>
      </SplitterPanel>
    </Splitter>
  </div>
</template>

<style scoped>
.build-visual-workspace {
  display: flex;
  flex-direction: column;
  height: 100%;
  min-height: 0;
}

.application-shell-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  padding: 0.7rem 1rem;
  border-bottom: 1px solid var(--p-surface-200);
  background: color-mix(in srgb, var(--p-surface-0) 96%, transparent);
  flex-shrink: 0;
}

.application-shell-header__title {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  min-width: 0;
}

.application-shell-header__label {
  font-size: 0.82rem;
  font-weight: 700;
  letter-spacing: 0.08em;
  text-transform: uppercase;
  color: var(--p-text-muted-color);
}

.application-shell-header__actions {
  display: flex;
  align-items: center;
  gap: 0.35rem;
  flex-wrap: wrap;
  justify-content: flex-end;
}

.application-shell-action {
  display: inline-flex;
  align-items: center;
  gap: 0.45rem;
  border: none;
  border-radius: 0.6rem;
  padding: 0.45rem 0.7rem;
  background: transparent;
  color: var(--p-text-muted-color);
  cursor: pointer;
  transition: all 0.15s ease;
}

.application-shell-action:hover:not(:disabled) {
  background: var(--p-surface-100);
  color: var(--p-text-color);
}

.application-shell-action:disabled {
  opacity: 0.45;
  cursor: default;
}

.application-shell-action i {
  font-size: 0.85rem;
}

.application-shell-action span {
  font-size: 0.85rem;
  font-weight: 500;
}

.visual-splitter {
  flex: 1;
  min-height: 0;
  contain: layout style paint;
  will-change: auto;
}

/* Connection status indicator */
.connection-status {
  display: inline-flex;
  align-items: center;
  gap: 0.35rem;
  padding: 0.2rem 0.6rem;
  border-radius: 999px;
  font-size: 0.75rem;
  font-weight: 500;
  cursor: default;
  border: 1px solid transparent;
}

.connection-status.connected {
  background: color-mix(in srgb, var(--p-green-500) 12%, transparent);
  border-color: color-mix(in srgb, var(--p-green-500) 30%, transparent);
  color: var(--p-green-600);
}

.connection-status.ui-only {
  background: color-mix(in srgb, var(--p-surface-400) 12%, transparent);
  border-color: color-mix(in srgb, var(--p-surface-400) 30%, transparent);
  color: var(--p-text-muted-color);
}

.connection-status.needs-runner {
  background: color-mix(in srgb, var(--p-yellow-500) 12%, transparent);
  border-color: color-mix(in srgb, var(--p-yellow-500) 30%, transparent);
  color: var(--p-yellow-700);
}

.status-dot {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  flex-shrink: 0;
}

.connected .status-dot { background: var(--p-green-500); }
.ui-only .status-dot { background: var(--p-surface-400); }
.needs-runner .status-dot { background: var(--p-yellow-500); }

/* Inline backend prompt */
.backend-prompt {
  position: absolute;
  bottom: 1rem;
  left: 50%;
  transform: translateX(-50%);
  z-index: 10;
  pointer-events: none;
  width: calc(100% - 2rem);
  max-width: 420px;
}

.backend-prompt-card {
  display: flex;
  align-items: flex-start;
  gap: 0.75rem;
  padding: 0.75rem 1rem;
  background: color-mix(in srgb, var(--p-yellow-500) 10%, var(--p-surface-0));
  border: 1px solid color-mix(in srgb, var(--p-yellow-500) 40%, transparent);
  border-radius: 10px;
  box-shadow: 0 4px 16px rgba(0,0,0,0.1);
  pointer-events: auto;
}

.backend-prompt-icon {
  font-size: 1rem;
  flex-shrink: 0;
  margin-top: 0.1rem;
}

.backend-prompt-body {
  flex: 1;
  min-width: 0;
}

.backend-prompt-title {
  font-size: 0.8125rem;
  font-weight: 600;
  color: var(--p-text-color);
  margin: 0 0 0.2rem;
}

.backend-prompt-desc {
  font-size: 0.75rem;
  color: var(--p-text-muted-color);
  margin: 0 0 0.6rem;
  line-height: 1.4;
}

.backend-prompt-actions {
  display: flex;
  gap: 0.5rem;
}

.prompt-btn {
  display: inline-flex;
  align-items: center;
  gap: 0.35rem;
  padding: 0.35rem 0.75rem;
  font-size: 0.75rem;
  font-weight: 500;
  border-radius: 6px;
  border: 1px solid transparent;
  cursor: pointer;
  transition: opacity 0.15s ease;
}

.prompt-btn:hover { opacity: 0.85; }

.prompt-btn--secondary {
  background: var(--p-surface-0);
  border-color: var(--p-surface-400);
  color: var(--p-text-color);
}

.prompt-btn--primary {
  background: var(--p-green-600);
  color: #fff;
}

/* Dismiss button on backend card */
.backend-prompt-dismiss {
  flex-shrink: 0;
  align-self: flex-start;
  background: none;
  border: none;
  color: var(--p-text-muted-color);
  cursor: pointer;
  padding: 0.2rem;
  line-height: 1;
  font-size: 0.7rem;
  opacity: 0.6;
  transition: opacity 0.15s ease;
}
.backend-prompt-dismiss:hover { opacity: 1; }

/* Idle hint banner */
.idle-hint {
  position: absolute;
  bottom: 0.75rem;
  left: 50%;
  transform: translateX(-50%);
  z-index: 10;
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.5rem 0.9rem;
  background: color-mix(in srgb, var(--p-blue-500) 10%, var(--p-surface-0));
  border: 1px solid color-mix(in srgb, var(--p-blue-500) 30%, transparent);
  border-radius: 8px;
  font-size: 0.75rem;
  color: var(--p-text-muted-color);
  white-space: nowrap;
  box-shadow: 0 2px 12px rgba(0,0,0,0.08);
  max-width: calc(100% - 2rem);
  white-space: normal;
}

.idle-hint i {
  font-size: 0.8rem;
  color: var(--p-blue-500);
  flex-shrink: 0;
}

.idle-hint-dismiss {
  background: none;
  border: none;
  font-size: 1rem;
  line-height: 1;
  color: var(--p-text-muted-color);
  cursor: pointer;
  padding: 0;
  margin-left: auto;
  opacity: 0.5;
  flex-shrink: 0;
}
.idle-hint-dismiss:hover { opacity: 1; }

.preview-only {
  display: flex;
  flex-direction: column;
  flex: 1;
  min-height: 0;
  overflow: hidden;
}


/* Show/Hide Logs toggle handle */
.advanced-toggle-handle {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0.375rem;
  align-self: center;
  min-width: 4.5rem;
  height: 1.25rem;
  margin: 0.2rem 0 0;
  padding: 0 0.55rem;
  border: none;
  border-radius: 999px;
  background: color-mix(in srgb, var(--p-surface-100) 80%, transparent);
  color: var(--p-text-muted-color);
  font-size: 0.68rem;
  font-weight: 500;
  cursor: pointer;
  flex-shrink: 0;
  transition: background 0.15s ease, color 0.15s ease, transform 0.15s ease;
  opacity: 0.9;
}

.advanced-toggle-handle:hover {
  background: color-mix(in srgb, var(--p-surface-200) 88%, transparent);
  color: var(--p-text-color);
}

.advanced-toggle-handle i {
  font-size: 0.55rem;
}

.advanced-toggle-handle-expanded {
  margin: 0.3rem auto 0.2rem;
}

.advanced-toggle-handle-dark {
  background: color-mix(in srgb, var(--p-surface-800) 88%, transparent);
}

.advanced-toggle-handle-dark:hover {
  background: color-mix(in srgb, var(--p-surface-700) 92%, transparent);
}

.splitter-panel {
  min-height: 0 !important;
  overflow: hidden;
  will-change: flex-basis;
  display: flex !important;
  flex-direction: column !important;
}

.preview-content {
  flex: 1;
  min-height: 0;
  display: flex;
  justify-content: center;
  align-items: center;
  overflow: auto;
  padding: 0.75rem;
  height: 100%;
}

.application-viewport {
  border: none;
  border-radius: 0;
  overflow: hidden;
  background: var(--p-surface-0);
  box-shadow: none;
  min-height: 0;
  transform: translateZ(0);
  position: relative;
}

.application-iframe {
  width: 100%;
  height: 100%;
  border: none;
}

.application-iframe--no-pointer {
  pointer-events: none;
}

.application-loading,
.application-empty {
  width: 100%;
  height: 100%;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  position: relative;
}

.application-loading::after {
  content: '';
  position: absolute;
  inset: 0;
  pointer-events: auto;
  z-index: 10;
}

.preview-resize-overlay {
  position: absolute;
  inset: 0;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  background: color-mix(in srgb, var(--p-surface-0) 80%, transparent);
  pointer-events: none;
}

.logs-panel {
  flex: 1;
  min-height: 0;
  overflow: hidden;
}

.logs-tabbed-container {
  display: flex;
  flex-direction: column;
  height: 100%;
  min-height: 0;
}

.logs-tab-bar {
  display: flex;
  align-items: center;
  gap: 0;
  border-bottom: 1px solid var(--p-surface-200);
  flex-shrink: 0;
}

.logs-tab {
  display: inline-flex;
  align-items: center;
  gap: 0.35rem;
  padding: 0.35rem 0.85rem;
  font-size: 0.75rem;
  font-weight: 500;
  color: var(--p-text-muted-color);
  background: none;
  border: none;
  border-bottom: 2px solid transparent;
  cursor: pointer;
  transition: color 0.15s ease, border-color 0.15s ease;
  white-space: nowrap;
}

.logs-tab--static {
  cursor: default;
}

.logs-tab:hover {
  color: var(--p-text-color);
}

.logs-tab.active {
  color: var(--p-primary-color);
  border-bottom-color: var(--p-primary-color);
}

.logs-tab-badge {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 1.1rem;
  height: 1.1rem;
  padding: 0 0.25rem;
  font-size: 0.65rem;
  font-weight: 600;
  border-radius: 999px;
  background: var(--p-primary-color);
  color: #fff;
  line-height: 1;
}

.logs-placeholder {
  height: 100%;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0.5rem;
  border-top: 1px solid var(--p-surface-200);
}

:global(.app-dark) .application-viewport {
  background: var(--p-surface-900);
}

:global(.app-dark) .application-shell-header {
  border-color: var(--p-surface-700);
  background: color-mix(in srgb, var(--p-surface-900) 94%, transparent);
}

:global(.app-dark) .application-shell-action:hover:not(:disabled) {
  background: var(--p-surface-800);
}


:global(.app-dark) .logs-placeholder {
  border-color: var(--p-surface-700);
}

:global(.app-dark) .logs-tab-bar {
  border-color: var(--p-surface-700);
}

:global(.app-dark) .logs-tab:hover {
  color: var(--p-text-color);
}

:global(.build-visual-workspace .p-splitter-gutter) {
  background-color: var(--p-surface-200);
}

:global(.build-visual-workspace .p-splitter-gutter:hover) {
  background-color: var(--p-primary-300);
}

:global(.app-dark .build-visual-workspace .p-splitter-gutter) {
  background-color: var(--p-surface-700);
}

:global(.app-dark .build-visual-workspace .p-splitter-gutter:hover) {
  background-color: var(--p-primary-600);
}

@media (max-width: 900px) {
  .application-shell-header {
    align-items: flex-start;
    flex-direction: column;
  }

  .application-shell-header__actions {
    width: 100%;
    justify-content: flex-start;
  }
}
</style>
