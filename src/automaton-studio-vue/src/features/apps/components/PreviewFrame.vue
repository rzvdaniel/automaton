<script setup lang="ts">
/**
 * PreviewFrame.vue
 * Live preview iframe component for the App Editor
 * Displays the app HTML with responsive viewport switching
 */
import { ref, computed, watch, onMounted } from 'vue'
import Button from 'primevue/button'
import SelectButton from 'primevue/selectbutton'
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
const currentSize = computed(() => PREVIEW_SIZES[editorStore.previewMode])

// Update iframe content when preview HTML changes
watch(
  () => editorStore.previewHtml,
  (newHtml) => {
    if (!iframeRef.value || !newHtml) return
    updateIframeContent(newHtml)
  },
)

// Initial load
onMounted(() => {
  if (editorStore.previewHtml && iframeRef.value) {
    updateIframeContent(editorStore.previewHtml)
  }
})

// Update iframe content using srcdoc approach
function updateIframeContent(html: string) {
  if (!iframeRef.value) return

  // Use document.write for better compatibility
  const doc = iframeRef.value.contentDocument
  if (doc) {
    doc.open()
    doc.write(html)
    doc.close()
  }
}

// Handle refresh button
async function handleRefresh() {
  await editorStore.refreshPreview()
}

// Handle mode change
function handleModeChange(mode: PreviewMode) {
  editorStore.setPreviewMode(mode)
}
</script>

<template>
  <div class="preview-frame-container">
    <!-- Toolbar -->
    <div class="preview-toolbar">
      <span class="font-semibold text-sm">Preview</span>
      <div class="flex align-items-center gap-2">
        <SelectButton
          :model-value="editorStore.previewMode"
          :options="previewModes"
          option-value="value"
          size="small"
          @update:model-value="handleModeChange"
        >
          <template #option="{ option }">
            <i :class="option.icon" v-tooltip.bottom="option.label"></i>
          </template>
        </SelectButton>
        <Button
          icon="pi pi-refresh"
          text
          rounded
          size="small"
          severity="secondary"
          :loading="editorStore.isLoadingPreview"
          @click="handleRefresh"
          v-tooltip.bottom="'Refresh Preview'"
        />
      </div>
    </div>

    <!-- Preview Content -->
    <div class="preview-content">
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
        <div v-else-if="!editorStore.previewHtml" class="preview-empty">
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

        <!-- Iframe -->
        <iframe
          v-else
          ref="iframeRef"
          class="preview-iframe"
          sandbox="allow-scripts allow-same-origin allow-forms allow-modals"
          title="App Preview"
        />
      </div>
    </div>
  </div>
</template>

<style scoped>
.preview-frame-container {
  height: 100%;
  display: flex;
  flex-direction: column;
  background-color: var(--p-surface-50);
}

.preview-toolbar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 0.5rem 1rem;
  background-color: var(--p-surface-0);
  border-bottom: 1px solid var(--p-surface-200);
  flex-shrink: 0;
}

.preview-content {
  flex: 1;
  overflow: auto;
  display: flex;
  align-items: flex-start;
  justify-content: center;
  padding: 1rem;
}

.preview-viewport {
  background-color: var(--p-surface-0);
  border: 1px solid var(--p-surface-200);
  border-radius: var(--p-border-radius);
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.1);
  transition: width 0.3s ease, height 0.3s ease;
  overflow: hidden;
  position: relative;
}

.preview-viewport.desktop {
  width: 100%;
  height: 100%;
  max-width: none;
}

.preview-viewport.tablet {
  width: 768px;
  height: 100%;
  max-height: 1024px;
}

.preview-viewport.mobile {
  width: 375px;
  height: 100%;
  max-height: 667px;
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
  min-height: 200px;
  text-align: center;
  padding: 2rem;
}

/* Dark mode */
:global(.app-dark) .preview-frame-container {
  background-color: var(--p-surface-900);
}

:global(.app-dark) .preview-toolbar {
  background-color: var(--p-surface-800);
  border-color: var(--p-surface-700);
}

:global(.app-dark) .preview-viewport {
  background-color: var(--p-surface-0);
  border-color: var(--p-surface-700);
}
</style>
