<script setup lang="ts">
import { computed } from 'vue'
import { useAppEditorStore } from '../stores/app-editor.store'

const editorStore = useAppEditorStore()

// Get file extension
function getFileExtension(path: string): string {
  const ext = path.split('.').pop()?.toLowerCase()
  return ext || 'file'
}

// Get human-readable file type
function getFileType(ext: string): string {
  const types: Record<string, string> = {
    html: 'HTML',
    css: 'CSS',
    js: 'JavaScript',
    ts: 'TypeScript',
    json: 'JSON',
    xml: 'XML',
    md: 'Markdown',
    txt: 'Text',
  }
  return types[ext] || ext.toUpperCase()
}

// Get file size in bytes
function getFileSize(content: string): string {
  const bytes = new Blob([content]).size
  if (bytes < 1024) return `${bytes}B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)}KB`
  return `${(bytes / (1024 * 1024)).toFixed(1)}MB`
}

// Computed properties
const currentFile = computed(() => editorStore.openFiles.find((f) => f.path === editorStore.activeFilePath))

const fileExtension = computed(() => (currentFile.value ? getFileExtension(currentFile.value.path) : ''))

const fileType = computed(() => (fileExtension.value ? getFileType(fileExtension.value) : 'No File'))

const fileSize = computed(() => (currentFile.value ? getFileSize(currentFile.value.content) : '—'))

const isDirty = computed(() => currentFile.value?.isDirty ?? false)

const unsavedCount = computed(() => editorStore.openFiles.filter((f) => f.isDirty).length)

const statusText = computed(() => {
  if (!currentFile.value) {
    return 'No file selected'
  }
  const scope = currentFile.value.scope ? `${currentFile.value.scope} • ` : ''
  const mode = currentFile.value.isReadOnly ? 'read-only • ' : ''
  return `${scope}${mode}${currentFile.value.path} • ${fileType.value} • ${fileSize.value}`
})
</script>

<template>
  <div class="status-bar">
    <!-- Left section: File info -->
    <div class="status-section left">
      <div class="status-item">
        <i v-if="isDirty" class="pi pi-circle-fill text-xs" style="color: var(--p-orange-500)"></i>
        <i v-else class="pi pi-circle-fill text-xs" style="color: transparent"></i>
        <span class="status-text">{{ statusText }}</span>
      </div>
    </div>

    <!-- Right section: Summary -->
    <div class="status-section right">
      <div v-if="unsavedCount > 0" class="status-item">
        <i class="pi pi-exclamation-circle text-xs" style="color: var(--p-orange-500)"></i>
        <span class="status-text">{{ unsavedCount }} unsaved</span>
      </div>
      <div v-if="editorStore.isPublished" class="status-item">
        <i class="pi pi-check-circle text-xs" style="color: var(--p-green-500)"></i>
        <span class="status-text">Published</span>
      </div>
      <div v-else class="status-item">
        <i class="pi pi-circle-fill text-xs" style="color: var(--p-orange-500)"></i>
        <span class="status-text">Draft</span>
      </div>
    </div>
  </div>
</template>

<style scoped>
.status-bar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 0.375rem 0.75rem;
  border-top: 1px solid var(--p-surface-200);
  height: 28px;
  flex-shrink: 0;
  font-size: 0.75rem;
  color: var(--p-text-muted-color);
}

.status-section {
  display: flex;
  align-items: center;
  gap: 1rem;
  min-width: 0;
}

.status-section.left {
  flex: 1;
  min-width: 0;
}

.status-section.right {
  flex-shrink: 0;
}

.status-item {
  display: flex;
  align-items: center;
  gap: 0.375rem;
  white-space: nowrap;
}

.status-text {
  font-size: 0.75rem;
  color: var(--p-text-muted-color);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.status-item i {
  line-height: 1;
}

/* Dark mode */
:global(.app-dark) .status-bar {
  border-top-color: var(--p-surface-700);
}
</style>
