<script setup lang="ts">
import Button from 'primevue/button'
import SelectButton from 'primevue/selectbutton'
import Tag from 'primevue/tag'
import { computed } from 'vue'
import { UiLogFilter, useAppEditorStore } from '../stores/app-editor.store'
import type { PreviewMode } from '../types/apps.types'
import { PREVIEW_SIZES } from '../types/apps.types'
import LiveUiLogPanel from './LiveUiLogPanel.vue'

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

    <div class="preview-content">
      <div
        class="preview-viewport"
        :class="editorStore.previewMode"
        :style="{
          width: currentSize.width,
          height: currentSize.height,
        }"
      >
        <div v-if="editorStore.isLoadingPreview" class="preview-loading">
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

    <LiveUiLogPanel class="logs-panel" />
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

.preview-content {
  flex: 1;
  min-height: 0;
  display: flex;
  justify-content: center;
  align-items: center;
  overflow: auto;
  padding: 0.75rem;
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
  max-width: 100%;
  max-height: 100%;
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
}

.logs-panel {
  height: 280px;
  flex-shrink: 0;
}

:global(.app-dark) .preview-header {
  border-color: var(--p-surface-700);
}

:global(.app-dark) .preview-viewport {
  border-color: var(--p-surface-700);
  background: var(--p-surface-900);
}
</style>
