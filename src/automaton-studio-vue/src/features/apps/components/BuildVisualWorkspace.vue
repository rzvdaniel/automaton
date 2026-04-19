<script setup lang="ts">
import Button from 'primevue/button'
import SelectButton from 'primevue/selectbutton'
import Splitter from 'primevue/splitter'
import SplitterPanel from 'primevue/splitterpanel'
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { UiLogFilter, useAppEditorStore } from '../stores/app-editor.store'
import type { PreviewMode } from '../types/apps.types'
import { PREVIEW_SIZES } from '../types/apps.types'
import ConsolePanel from './ConsolePanel.vue'
import LiveUiLogPanel from './LiveUiLogPanel.vue'

const props = withDefaults(
  defineProps<{
    suspendHeavyRendering?: boolean
    runnerId?: string | null
  }>(),
  {
    suspendHeavyRendering: false,
    runnerId: null,
  },
)

const editorStore = useAppEditorStore()
const router = useRouter()

const previewModes = [
  { icon: 'pi pi-desktop', value: 'desktop' as PreviewMode, label: 'Desktop' },
  { icon: 'pi pi-tablet', value: 'tablet' as PreviewMode, label: 'Tablet' },
  { icon: 'pi pi-mobile', value: 'mobile' as PreviewMode, label: 'Mobile' },
]

const currentSize = computed(() => PREVIEW_SIZES[editorStore.previewMode] ?? PREVIEW_SIZES.desktop)
const uiTestEntries = computed(() =>
  editorStore.uiLogEntries.filter((entry) => entry.message.startsWith('[AI UI Test]')),
)
const uiPassCount = computed(() =>
  uiTestEntries.value.filter((entry) => !entry.message.includes('FAILED')).length,
)
const uiFailCount = computed(() =>
  uiTestEntries.value.filter((entry) => entry.message.includes('FAILED')).length,
)
const lastUiTestAction = computed(() => {
  const first = uiTestEntries.value[0]
  if (!first) return null
  const marker = '[AI UI Test] '
  const message = first.message.startsWith(marker)
    ? first.message.slice(marker.length)
    : first.message
  return message.split(' — ')[0]
})

function handleModeChange(mode: PreviewMode) {
  editorStore.setPreviewMode(mode)
}

function focusAllLogs() {
  editorStore.setUiLogFilter(UiLogFilter.All)
}

function focusFailedLogs() {
  editorStore.setUiLogFilter(UiLogFilter.Errors)
}

function focusPassedLogs() {
  editorStore.setUiLogFilter(UiLogFilter.Runtime)
}

const isPassFilterActive = computed(() => editorStore.uiLogFilter === UiLogFilter.Runtime)
const isFailFilterActive = computed(() => editorStore.uiLogFilter === UiLogFilter.Errors)
const isAllFilterActive = computed(() => editorStore.uiLogFilter === UiLogFilter.All)

const SPLITTER_STORAGE_KEY = 'app-editor-build-visual-splitter'
const defaultSplitterSizes = [74, 26]

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

function setSplitterPreset(previewSize: number, logsSize: number): void {
  splitterSizes.value = normalizeSizes([previewSize, logsSize])
  saveSplitterSizes(splitterSizes.value)
  splitterRenderKey.value += 1
}

function maximizePreview(): void {
  setSplitterPreset(90, 10)
}

function handleSplitterResizing(): void {
  isLocalResizing.value = true
}

const isHeavyRenderingSuspended = computed(() => props.suspendHeavyRendering || isLocalResizing.value)
const isAdvancedExpanded = ref(false)
const activeLogTab = ref<'ui-logs' | 'backend-logs'>('ui-logs')

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
  () => editorStore.previewUrl,
  (url) => {
    if (url && showBackendPrompt.value) startIdleTimer()
  },
)

onMounted(() => {
  if (editorStore.previewUrl && showBackendPrompt.value) startIdleTimer()
})

onUnmounted(() => {
  if (idleTimer) clearTimeout(idleTimer)
})

const previewViewportStyle = computed(() => {
  if (editorStore.previewMode === 'desktop') {
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
</script>

<template>
  <div class="build-visual-workspace">
    <!-- Controls bar -->
    <div class="preview-header">
      <div class="flex align-items-center gap-2">
        <!-- Connection status dot — clickable micro-CTA when backend needed -->
        <div
          class="connection-status"
          :class="backendConnected ? 'connected' : (editorStore.hasBackendFolder ? 'needs-runner' : 'ui-only')"
          :style="editorStore.hasBackendFolder && !backendConnected ? 'cursor:pointer' : ''"
          v-tooltip.bottom="backendConnected ? 'Backend connected' : (editorStore.hasBackendFolder ? 'UI preview is free · Backend execution requires Pro' : 'This app has no backend')"
          @click="editorStore.hasBackendFolder && !backendConnected ? (backendCardDismissed = false) : undefined"
        >
          <span class="status-dot"></span>
          <span class="status-label">
            <template v-if="backendConnected">Live</template>
            <template v-else-if="editorStore.hasBackendFolder">Backend needed · <u>Set up</u></template>
            <template v-else>UI only</template>
          </span>
        </div>
        <SelectButton
          :model-value="editorStore.previewMode"
          :options="previewModes"
          option-value="value"
          :allow-empty="false"
          @update:model-value="handleModeChange"
        >
          <template #option="{ option }">
            <div class="flex align-items-center gap-2">
              <i :class="option.icon"></i>
              <span>{{ option.label }}</span>
            </div>
          </template>
        </SelectButton>
        <Button
          icon="pi pi-refresh"
          text
          severity="secondary"
          :loading="editorStore.isLoadingPreview"
          @click="editorStore.refreshPreview()"
        />
      </div>
    </div>

    <!-- Collapsed: preview fills all space -->
    <div v-if="!isAdvancedExpanded" class="preview-only">
      <div class="preview-content">
        <div
          class="preview-viewport"
          :class="editorStore.previewMode"
          :style="previewViewportStyle"
        >
          <div v-if="isHeavyRenderingSuspended" class="preview-loading">
            <iframe
              v-if="editorStore.previewUrl"
              :key="editorStore.previewUrl"
              :src="editorStore.previewUrl"
              class="preview-iframe"
              sandbox="allow-scripts allow-same-origin allow-forms allow-modals allow-downloads"
              title="App Preview"
            />
            <div class="preview-resize-overlay">
              <i class="pi pi-arrows-v text-3xl text-color-secondary"></i>
              <p class="mt-2 text-color-secondary">Resizing preview...</p>
            </div>
          </div>
          <div v-else-if="editorStore.isLoadingPreview" class="preview-loading">
            <i class="pi pi-spin pi-spinner text-3xl"></i>
            <p class="mt-2">Loading preview...</p>
          </div>
          <div v-else-if="!editorStore.previewUrl" class="preview-empty">
            <i class="pi pi-eye-slash text-3xl text-color-secondary"></i>
            <p class="mt-2 text-color-secondary">No preview available</p>
          </div>
          <iframe
              v-else
              :key="editorStore.previewUrl"
              :src="editorStore.previewUrl"
              class="preview-iframe"
              :class="{ 'preview-iframe--no-pointer': isHeavyRenderingSuspended }"
              sandbox="allow-scripts allow-same-origin allow-forms allow-modals allow-downloads"
              title="App Preview"
            />
            <!-- Inline backend prompt (non-blocking overlay at bottom of iframe) -->
            <div v-if="showBackendCard" class="backend-prompt">
              <div class="backend-prompt-card">
                <div class="backend-prompt-icon">⚡</div>
                <div class="backend-prompt-body">
                  <p class="backend-prompt-title">Backend execution requires Pro</p>
                  <p class="backend-prompt-desc">UI preview is free. Running live logic, APIs, and workflows requires a Pro plan or a self-hosted runner.</p>
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
      <!-- Logs toggle bar (collapsed) -->
      <button class="advanced-toggle-bar" @click="isAdvancedExpanded = true">
        <i class="pi pi-chevron-up"></i>
        <span>Show Logs</span>
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
            class="preview-viewport"
            :class="editorStore.previewMode"
            :style="previewViewportStyle"
          >
            <div v-if="isHeavyRenderingSuspended" class="preview-loading">
              <iframe
                v-if="editorStore.previewUrl"
                :key="editorStore.previewUrl"
                :src="editorStore.previewUrl"
                class="preview-iframe"
                sandbox="allow-scripts allow-same-origin allow-forms allow-modals allow-downloads"
                title="App Preview"
              />
              <div class="preview-resize-overlay">
                <i class="pi pi-arrows-v text-3xl text-color-secondary"></i>
                <p class="mt-2 text-color-secondary">Resizing preview...</p>
              </div>
            </div>
            <div v-else-if="editorStore.isLoadingPreview" class="preview-loading">
              <i class="pi pi-spin pi-spinner text-3xl"></i>
              <p class="mt-2">Loading preview...</p>
            </div>
            <div v-else-if="!editorStore.previewUrl" class="preview-empty">
              <i class="pi pi-eye-slash text-3xl text-color-secondary"></i>
              <p class="mt-2 text-color-secondary">No preview available</p>
            </div>
            <iframe
              v-else
              :key="editorStore.previewUrl"
              :src="editorStore.previewUrl"
              class="preview-iframe"
              :class="{ 'preview-iframe--no-pointer': isHeavyRenderingSuspended }"
              sandbox="allow-scripts allow-same-origin allow-forms allow-modals allow-downloads"
              title="App Preview"
            />
            <!-- Inline backend prompt (non-blocking overlay at bottom of iframe) -->
            <div v-if="showBackendCard" class="backend-prompt">
              <div class="backend-prompt-card">
                <div class="backend-prompt-icon">⚡</div>
                <div class="backend-prompt-body">
                  <p class="backend-prompt-title">Backend execution requires Pro</p>
                  <p class="backend-prompt-desc">UI preview is free. Running live logic, APIs, and workflows requires a Pro plan or a self-hosted runner.</p>
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
        <!-- Logs toggle bar (expanded) -->
        <button class="advanced-toggle-bar" @click="isAdvancedExpanded = false">
          <i class="pi pi-chevron-down"></i>
          <span>Hide Logs</span>
        </button>
        <div v-if="isHeavyRenderingSuspended" class="logs-placeholder">
          <i class="pi pi-arrows-v text-color-secondary"></i>
          <span class="text-sm text-color-secondary">Resizing logs...</span>
        </div>
        <div v-else class="logs-tabbed-container">
          <div class="logs-tab-bar">
            <button
              class="logs-tab"
              :class="{ active: activeLogTab === 'ui-logs' }"
              @click="activeLogTab = 'ui-logs'"
            >
              <i class="pi pi-wave-pulse"></i>
              Live UI Logs
            </button>
            <button
              class="logs-tab"
              :class="{ active: activeLogTab === 'backend-logs' }"
              @click="activeLogTab = 'backend-logs'"
            >
              <i class="pi pi-server"></i>
              Live Backend Logs
              <span v-if="editorStore.consoleEntries.length > 0" class="logs-tab-badge">{{ editorStore.consoleEntries.length }}</span>
            </button>
          </div>
          <LiveUiLogPanel v-show="activeLogTab === 'ui-logs'" class="logs-panel" />
          <ConsolePanel v-show="activeLogTab === 'backend-logs'" class="logs-panel" />
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

.preview-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 0.5rem 0.75rem;
  border-bottom: 1px solid var(--p-surface-200);
  flex-shrink: 0;
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


/* Show/Hide Logs toggle bar */
.advanced-toggle-bar {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0.375rem;
  width: 100%;
  padding: 0.4rem 1rem;
  border: none;
  border-top: 1px solid var(--p-surface-200);
  background: var(--p-surface-50);
  color: var(--p-text-muted-color);
  font-size: 0.8rem;
  cursor: pointer;
  flex-shrink: 0;
  transition: background 0.15s ease, color 0.15s ease;
}

.advanced-toggle-bar:hover {
  background: var(--p-surface-100);
  color: var(--p-text-color);
}

.advanced-toggle-bar i {
  font-size: 0.7rem;
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

.preview-viewport {
  border: 1px solid var(--p-surface-200);
  border-radius: var(--p-border-radius);
  overflow: hidden;
  background: var(--p-surface-0);
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.08);
  min-height: 0;
  transform: translateZ(0);
  position: relative;
}

.preview-iframe {
  width: 100%;
  height: 100%;
  border: none;
}

.preview-iframe--no-pointer {
  pointer-events: none;
}

.preview-loading,
.preview-empty {
  width: 100%;
  height: 100%;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  position: relative;
}

.preview-loading::after {
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

:global(.app-dark) .preview-header {
  border-color: var(--p-surface-700);
}

:global(.app-dark) .advanced-toggle-bar {
  background: var(--p-surface-800);
  border-color: var(--p-surface-700);
}

:global(.app-dark) .advanced-toggle-bar:hover {
  background: var(--p-surface-700);
}

:global(.app-dark) .preview-viewport {
  border-color: var(--p-surface-700);
  background: var(--p-surface-900);
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
</style>
