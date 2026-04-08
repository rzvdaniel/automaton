<script setup lang="ts">
/**
 * PreviewOverlay.vue
 * Full-screen preview overlay for the App Editor
 * Provides responsive viewport switching with maximum visibility
 */
import { ref, computed, onMounted, onUnmounted } from 'vue'
import Button from 'primevue/button'
import SelectButton from 'primevue/selectbutton'
import ToggleButton from 'primevue/togglebutton'
import Tag from 'primevue/tag'
import { useAppEditorStore } from '../stores/app-editor.store'
import type { PreviewMode } from '../types/apps.types'
import { PREVIEW_SIZES } from '../types/apps.types'

const editorStore = useAppEditorStore()
const iframeRef = ref<HTMLIFrameElement>()

// Preview mode options
const previewModes = ref([
  { icon: 'pi pi-desktop', value: 'desktop' as PreviewMode, label: 'Desktop' },
  { icon: 'pi pi-tablet', value: 'tablet' as PreviewMode, label: 'Tablet' },
  { icon: 'pi pi-mobile', value: 'mobile' as PreviewMode, label: 'Mobile' },
])

// Current viewport size
const currentSize = computed(() => PREVIEW_SIZES[editorStore.previewMode] ?? PREVIEW_SIZES.desktop)
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

// Initial load
onMounted(() => {
  // Prevent body scroll when overlay is open
  document.body.style.overflow = 'hidden'
})

onUnmounted(() => {
  // Restore body scroll
  document.body.style.overflow = ''
})

// Handle refresh button
async function handleRefresh() {
  await editorStore.refreshPreview()
}

// Handle mode change
function handleModeChange(mode: PreviewMode) {
  editorStore.setPreviewMode(mode)
}

// Handle close
function handleClose() {
  editorStore.closePreviewOverlay()
}

// Handle keyboard shortcuts
function handleKeyDown(e: KeyboardEvent) {
  if (e.key === 'Escape') {
    handleClose()
  }
}

onMounted(() => {
  window.addEventListener('keydown', handleKeyDown)
})

onUnmounted(() => {
  window.removeEventListener('keydown', handleKeyDown)
})
</script>

<template>
  <Teleport to="body">
    <div class="preview-overlay" @click.self="handleClose">
      <!-- Header -->
      <div class="preview-overlay-header">
        <div class="header-left">
          <Button
            v-tooltip.bottom="'Back to Editor (Esc)'"
            icon="pi pi-arrow-left"
            text
            rounded
            severity="secondary"
            @click="handleClose"
          />
          <span class="app-name">{{ editorStore.currentApp?.name }}</span>
          <span class="preview-badge">Preview</span>
        </div>

        <div class="header-center">
          <div class="header-center-controls">
            <Tag
              :value="backendModeLabel"
              :severity="editorStore.previewUseMockBackend ? 'warning' : 'success'"
              rounded
            />
            <ToggleButton
              v-model="isMockBackendEnabled"
              on-label="Real"
              off-label="Mock"
              on-icon="pi pi-server"
              off-icon="pi pi-clone"
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
          </div>
        </div>

        <div class="header-right">
          <Button
            icon="pi pi-refresh"
            label="Refresh"
            text
            severity="secondary"
            :loading="editorStore.isLoadingPreview"
            @click="handleRefresh"
          />
          <Button
            icon="pi pi-times"
            label="Close"
            severity="secondary"
            outlined
            @click="handleClose"
          />
        </div>
      </div>

      <!-- Preview Content -->
      <div class="preview-overlay-content">
          <div v-if="editorStore.previewWarning" class="preview-warning">
            <i class="pi pi-exclamation-triangle"></i>
            <span>{{ editorStore.previewWarning }}</span>
            <Button
              icon="pi pi-times"
              text
              rounded
              size="small"
              severity="secondary"
              aria-label="Dismiss warning"
              @click="editorStore.setPreviewWarning(null)"
            />
          </div>
        <div
          class="preview-viewport"
          :class="editorStore.previewMode"
          :style="{
            width: currentSize.width,
            height: currentSize.height,
          }"
        >
          <!-- Loading state -->
          <div v-if="editorStore.isLoadingPreview" class="preview-loading">
            <i class="pi pi-spin pi-spinner text-4xl"></i>
            <p class="mt-2">Loading preview...</p>
          </div>

          <!-- Empty state -->
          <div v-else-if="!editorStore.previewUrl" class="preview-empty">
            <i class="pi pi-eye text-4xl text-color-secondary"></i>
            <p class="mt-2 text-color-secondary">No preview available</p>
            <Button
              label="Refresh"
              icon="pi pi-refresh"
              size="small"
              outlined
              @click="handleRefresh"
            />
          </div>

          <!-- Iframe with HTTP-based preview -->
          <iframe
            v-else
            ref="iframeRef"
            :key="editorStore.previewUrl"
            :src="editorStore.previewUrl"
            class="preview-iframe"
            sandbox="allow-scripts allow-same-origin allow-forms allow-modals allow-downloads"
            title="App Preview"
          />
        </div>
      </div>
    </div>
  </Teleport>
</template>

<style scoped>
.preview-overlay {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  z-index: 1000;
  background-color: var(--p-surface-ground);
  display: flex;
  flex-direction: column;
}

.preview-overlay-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 0.75rem 1rem;
  background-color: var(--p-surface-0);
  border-bottom: 1px solid var(--p-surface-200);
  flex-shrink: 0;
}

.header-left {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}

.app-name {
  font-weight: 600;
  font-size: 1rem;
}

.preview-badge {
  background-color: var(--p-primary-100);
  color: var(--p-primary-700);
  padding: 0.25rem 0.5rem;
  border-radius: var(--p-border-radius);
  font-size: 0.75rem;
  font-weight: 500;
}

.header-center {
  display: flex;
  justify-content: center;
}

.header-center-controls {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.header-right {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.preview-overlay-content {
  flex: 1;
  overflow: auto;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 1.5rem;
  background-color: var(--p-surface-100);
  flex-direction: column;
}

.preview-warning {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  width: 100%;
  max-width: 720px;
  margin-bottom: 0.75rem;
  padding: 0.5rem 0.75rem;
  background-color: var(--p-orange-50);
  color: var(--p-orange-700);
  border: 1px solid var(--p-orange-200);
  border-radius: var(--p-border-radius);
  font-size: 0.85rem;
}

.preview-warning > span {
  flex: 1;
  min-width: 0;
  word-break: break-word;
}

.preview-viewport {
  background-color: var(--p-surface-0);
  border: 1px solid var(--p-surface-300);
  border-radius: var(--p-border-radius);
  box-shadow: 0 4px 20px rgba(0, 0, 0, 0.15);
  transition: width 0.3s ease, height 0.3s ease;
  overflow: hidden;
  position: relative;
}

.preview-viewport.desktop {
  width: 100%;
  height: 100%;
  max-width: none;
  max-height: none;
}

.preview-viewport.tablet {
  width: 768px;
  height: calc(100% - 2rem);
  max-height: 1024px;
}

.preview-viewport.mobile {
  width: 375px;
  height: calc(100% - 2rem);
  max-height: 812px;
}

.preview-iframe {
  width: 100%;
  height: 100%;
  border: none;
  display: block;
}

.preview-loading,
.preview-empty {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  height: 100%;
  min-height: 300px;
  text-align: center;
  padding: 2rem;
}

/* Dark mode */
:global(.app-dark) .preview-overlay {
  background-color: var(--p-surface-900);
}

:global(.app-dark) .preview-overlay-header {
  background-color: var(--p-surface-800);
  border-color: var(--p-surface-700);
}

:global(.app-dark) .preview-badge {
  background-color: var(--p-primary-900);
  color: var(--p-primary-300);
}

:global(.app-dark) .preview-overlay-content {
  background-color: var(--p-surface-800);
}

:global(.app-dark) .preview-viewport {
  background-color: var(--p-surface-0);
  border-color: var(--p-surface-600);
}
</style>
