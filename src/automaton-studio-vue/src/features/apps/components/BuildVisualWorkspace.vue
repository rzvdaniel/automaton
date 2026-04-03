<script setup lang="ts">
import Button from 'primevue/button'
import SelectButton from 'primevue/selectbutton'
import Tag from 'primevue/tag'
import ToggleButton from 'primevue/togglebutton'
import Splitter from 'primevue/splitter'
import SplitterPanel from 'primevue/splitterpanel'
import { computed, ref } from 'vue'
import { UiLogFilter, useAppEditorStore } from '../stores/app-editor.store'
import type { PreviewMode } from '../types/apps.types'
import { PREVIEW_SIZES } from '../types/apps.types'
import LiveUiLogPanel from './LiveUiLogPanel.vue'

const props = withDefaults(
  defineProps<{
    suspendHeavyRendering?: boolean
  }>(),
  {
    suspendHeavyRendering: false,
  },
)

const editorStore = useAppEditorStore()

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
const isMockBackendEnabled = computed({
  get: () => editorStore.previewUseMockBackend,
  set: (value: boolean) => {
    editorStore.setPreviewBackendMode(value ? 'mock' : 'real')
    void editorStore.refreshPreview()
  },
})
const backendModeLabel = computed(() =>
  editorStore.previewUseMockBackend ? 'Mock Backend' : 'Real Backend',
)

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
    <div class="preview-header">
      <div class="flex align-items-center gap-2">
        <i class="pi pi-eye text-color-secondary"></i>
        <span class="font-semibold">Live Preview</span>
        <div class="ui-test-summary">
          <Tag :value="`Pass ${uiPassCount}`" severity="success" rounded class="summary-tag clickable" :class="{ active: isPassFilterActive }" @click="focusPassedLogs" />
          <Tag :value="`Fail ${uiFailCount}`" :severity="uiFailCount > 0 ? 'danger' : 'secondary'" rounded class="summary-tag clickable" :class="{ active: isFailFilterActive }" @click="focusFailedLogs" />
          <Tag value="All Logs" severity="secondary" rounded class="summary-tag clickable" :class="{ active: isAllFilterActive }" @click="focusAllLogs" />
          <span v-if="lastUiTestAction" class="last-action text-sm text-color-secondary">
            Last: {{ lastUiTestAction }}
          </span>
        </div>
      </div>
      <div class="flex align-items-center gap-2">
        <Tag
          :value="backendModeLabel"
          :severity="editorStore.previewUseMockBackend ? 'warning' : 'success'"
          rounded
        />
        <ToggleButton
          v-model="isMockBackendEnabled"
          on-label="Mock"
          off-label="Real"
          on-icon="pi pi-clone"
          off-icon="pi pi-server"
          :disabled="editorStore.isLoadingPreview"
          aria-label="Toggle preview backend mode"
        />
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
        <Button
          icon="pi pi-arrows-alt-v"
          text
          severity="secondary"
          v-tooltip.bottom="'Maximize Preview (90/10)'"
          @click="maximizePreview"
        />
      </div>
    </div>

    <Splitter
      :key="splitterRenderKey"
      class="visual-splitter"
      layout="vertical"
      :gutter-size="8"
      @resize="handleSplitterResizing"
      @resizeend="handleSplitterResize"
    >
      <SplitterPanel :size="splitterSizes[0]" :min-size="45" class="splitter-panel">
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
              sandbox="allow-scripts allow-same-origin allow-forms allow-modals allow-downloads"
              title="App Preview"
            />
          </div>
        </div>
      </SplitterPanel>

      <SplitterPanel :size="splitterSizes[1]" :min-size="12" class="splitter-panel">
        <div v-if="isHeavyRenderingSuspended" class="logs-placeholder">
          <i class="pi pi-arrows-v text-color-secondary"></i>
          <span class="text-sm text-color-secondary">Resizing logs...</span>
        </div>
        <LiveUiLogPanel v-else class="logs-panel" />
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
}

.splitter-panel {
  min-height: 0 !important;
  overflow: hidden;
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

.ui-test-summary {
  display: inline-flex;
  align-items: center;
  gap: 0.375rem;
  margin-left: 0.5rem;
}

.last-action {
  margin-left: 0.25rem;
  max-width: 280px;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.summary-tag.clickable {
  cursor: pointer;
}

.summary-tag.clickable.active {
  outline: 2px solid var(--p-primary-400);
  outline-offset: 1px;
  box-shadow: 0 0 0 1px var(--p-primary-100) inset;
}

.preview-viewport {
  border: 1px solid var(--p-surface-200);
  border-radius: var(--p-border-radius);
  overflow: hidden;
  background: var(--p-surface-0);
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.08);
  min-height: 0;
}

.preview-iframe {
  width: 100%;
  height: 100%;
  border: none;
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
  height: 100%;
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

:global(.app-dark) .preview-viewport {
  border-color: var(--p-surface-700);
  background: var(--p-surface-900);
}

:global(.app-dark) .logs-placeholder {
  border-color: var(--p-surface-700);
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
