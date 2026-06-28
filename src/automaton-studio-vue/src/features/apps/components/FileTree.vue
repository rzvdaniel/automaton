<script setup lang="ts">
/**
 * FileTree.vue
 * File explorer component for the App Editor
 * Displays hierarchical file structure with icons and context menu
 */
import { ref, computed } from 'vue'
import Tree from 'primevue/tree'
import Button from 'primevue/button'
import { useAppEditorStore } from '../stores/app-editor.store'
import type { FileNode, PendingFileChangeType } from '../types/apps.types'

const props = withDefaults(defineProps<{
  title?: string
  nodes?: FileNode[]
  activePath?: string | null
  dirtyPaths?: string[]
  pendingChanges?: Record<string, PendingFileChangeType>
  showCreate?: boolean
  allowDelete?: boolean
  emptyMessage?: string
}>(), {
  title: 'Files',
  nodes: undefined,
  activePath: undefined,
  dirtyPaths: undefined,
  pendingChanges: undefined,
  showCreate: true,
  allowDelete: true,
  emptyMessage: 'No files yet',
})

const emit = defineEmits<{
  createFile: []
  createFolder: []
  openFile: [path: string]
  deleteFile: [path: string]
  deleteFolder: [path: string]
  deleteSelection: [nodes: Array<{ path: string; type: FileNode['type'] }>]
  renameFile: [path: string]
}>()

const editorStore = useAppEditorStore()

// Tree selection
const selectedKeys = ref<Record<string, boolean>>({})

// Convert FileNode[] to PrimeVue Tree format
const treeNodes = computed(() => {
  return convertToTreeNodes(fileTree.value)
})

const fileTree = computed(() => props.nodes ?? editorStore.fileTree)

function convertToTreeNodes(nodes: FileNode[]): any[] {
  return nodes.map((node) => ({
    key: node.key,
    label: node.name,
    data: node,
    icon: node.type === 'folder' ? 'pi pi-folder' : getFileIcon(node.extension || ''),
    children: node.children ? convertToTreeNodes(node.children) : undefined,
    leaf: node.type === 'file',
  }))
}

const selectedEntries = computed(() => {
  const selected = new Set(Object.keys(selectedKeys.value).filter((key) => selectedKeys.value[key]))
  if (selected.size === 0) {
    return []
  }

  const matches: Array<{ path: string; type: FileNode['type'] }> = []

  function walk(nodes: FileNode[]) {
    for (const node of nodes) {
      if (selected.has(node.key)) {
        matches.push({
          path: node.path,
          type: node.type,
        })
      }

      if (node.children?.length) {
        walk(node.children)
      }
    }
  }

  walk(fileTree.value)
  return matches
})

const deletableSelectedEntries = computed(() =>
  selectedEntries.value.filter((entry) =>
    entry.type === 'folder' ? canDeleteFolder(entry.path) : canDeleteFile(entry.path),
  ),
)

const selectedDeleteLabel = computed(() => {
  const count = deletableSelectedEntries.value.length
  return count > 1 ? `Delete ${count} Selected` : 'Delete Selected'
})

function handleDeleteSelection() {
  if (deletableSelectedEntries.value.length === 0) {
    return
  }

  emit('deleteSelection', deletableSelectedEntries.value)
  selectedKeys.value = {}
}

function getFileIcon(extension: string): string {
  switch (extension.toLowerCase()) {
    case 'html':
    case 'htm':
      return 'pi pi-file'
    case 'css':
      return 'pi pi-palette'
    case 'js':
    case 'ts':
      return 'pi pi-code'
    case 'json':
      return 'pi pi-file'
    case 'svg':
    case 'png':
    case 'jpg':
    case 'jpeg':
    case 'gif':
      return 'pi pi-image'
    default:
      return 'pi pi-file'
  }
}

// Handle node selection (click)
function onNodeSelect(node: any) {
  const data = node.data
  const fileNode = data as FileNode
  if (fileNode.type === 'file') {
    emit('openFile', fileNode.path)
    if (!props.nodes) {
      editorStore.openFile(fileNode.path)
    }
  }
}

// Check if a file is active
function isActive(path: string): boolean {
  return (props.activePath ?? editorStore.activeFilePath) === path
}

// Check if a file has unsaved changes
function isDirty(path: string): boolean {
  if (props.dirtyPaths) {
    return props.dirtyPaths.includes(path)
  }

  return editorStore.openFiles.some((f) => f.path === path && f.isDirty)
}

function getPendingChangeType(path: string | undefined): PendingFileChangeType | null {
  if (!path || !props.pendingChanges) {
    return null
  }

  return props.pendingChanges[path] ?? null
}

const pendingChangeEntries = computed(() => {
  if (!props.pendingChanges) {
    return []
  }

  return Object.entries(props.pendingChanges).map(([path, type]) => ({ path, type }))
})

const pendingChangeCount = computed(() => pendingChangeEntries.value.length)
const pendingModifiedEntries = computed(() => pendingChangeEntries.value.filter((entry) => entry.type === 'modified'))
const pendingAddedEntries = computed(() => pendingChangeEntries.value.filter((entry) => entry.type === 'added'))
const pendingDeletedEntries = computed(() => pendingChangeEntries.value.filter((entry) => entry.type === 'deleted'))
const isPendingChangesOpen = ref(false)

function getChangeMarker(type: PendingFileChangeType | null): string | null {
  switch (type) {
    case 'modified':
      return '●'
    case 'added':
      return '+'
    case 'deleted':
      return '-'
    default:
      return null
  }
}

function getPathLabel(path: string): string {
  return path.replace(/^source\//, '')
}

function canDeleteFile(path: string | undefined): boolean {
  if (!props.allowDelete) return false
  if (!path) return false
  const normalizedPath = path.replace(/\\/g, '/')
  const scope = normalizedPath.startsWith('source/')
    ? 'source'
    : normalizedPath.startsWith('live/')
      ? 'live'
      : normalizedPath.startsWith('versions/')
        ? 'versions'
        : null

  if (scope === 'versions' || scope === null) {
    return false
  }

  return normalizedPath !== 'source/index.html' &&
    normalizedPath !== 'source/frontend/index.html' &&
    normalizedPath !== 'source/_metadata.json' &&
    normalizedPath !== '_metadata.json'
}

function canDeleteFolder(path: string | undefined): boolean {
  if (!props.allowDelete) return false
  if (!path) return false
  const normalizedPath = path.replace(/\\/g, '/').replace(/\/+$/, '')
  const scope = normalizedPath.startsWith('source/')
    ? 'source'
    : normalizedPath.startsWith('live/')
      ? 'live'
      : normalizedPath.startsWith('versions/')
        ? 'versions'
        : null

  if (scope === 'versions' || scope === null) {
    return false
  }

  return normalizedPath !== 'source' &&
    normalizedPath !== 'live' &&
    normalizedPath !== 'source/frontend' &&
    normalizedPath !== 'source/backend' &&
    normalizedPath !== 'live/frontend' &&
    normalizedPath !== 'live/backend'
}
</script>

<template>
  <div class="file-tree">
    <!-- Header -->
    <div class="file-tree-header">
      <span class="font-semibold">{{ title }}</span>
      <div class="file-tree-header-actions">
        <slot name="header-actions" />
        <Button
          v-if="showCreate"
          v-tooltip.bottom="'New File'"
          icon="pi pi-plus"
          text
          rounded
          size="small"
          severity="secondary"
          @click="emit('createFile')"
        />
        <Button
          v-if="allowDelete"
          :disabled="deletableSelectedEntries.length === 0"
          :label="deletableSelectedEntries.length > 1 ? selectedDeleteLabel : undefined"
          v-tooltip.bottom="deletableSelectedEntries.length <= 1 ? 'Delete selected items' : undefined"
          icon="pi pi-trash"
          text
          rounded
          size="small"
          severity="secondary"
          @click="handleDeleteSelection"
        />
      </div>
    </div>

    <div v-if="pendingChangeCount > 0" class="file-tree-pending">
      <button type="button" class="file-tree-pending__summary" @click="isPendingChangesOpen = !isPendingChangesOpen">
        <span>{{ pendingChangeCount }} pending {{ pendingChangeCount === 1 ? 'change' : 'changes' }}</span>
        <i :class="['pi', isPendingChangesOpen ? 'pi-chevron-up' : 'pi-chevron-down']"></i>
      </button>
      <div v-if="isPendingChangesOpen" class="file-tree-pending__details">
        <div v-if="pendingModifiedEntries.length > 0" class="file-tree-pending__group">
          <div class="file-tree-pending__label">Modified ({{ pendingModifiedEntries.length }})</div>
          <div v-for="entry in pendingModifiedEntries" :key="`modified-${entry.path}`" class="file-tree-pending__entry">
            <span class="file-tree-pending__marker file-tree-pending__marker--modified">●</span>
            <span>{{ getPathLabel(entry.path) }}</span>
          </div>
        </div>
        <div v-if="pendingAddedEntries.length > 0" class="file-tree-pending__group">
          <div class="file-tree-pending__label">Added ({{ pendingAddedEntries.length }})</div>
          <div v-for="entry in pendingAddedEntries" :key="`added-${entry.path}`" class="file-tree-pending__entry">
            <span class="file-tree-pending__marker file-tree-pending__marker--added">+</span>
            <span>{{ getPathLabel(entry.path) }}</span>
          </div>
        </div>
        <div v-if="pendingDeletedEntries.length > 0" class="file-tree-pending__group">
          <div class="file-tree-pending__label">Deleted ({{ pendingDeletedEntries.length }})</div>
          <div v-for="entry in pendingDeletedEntries" :key="`deleted-${entry.path}`" class="file-tree-pending__entry">
            <span class="file-tree-pending__marker file-tree-pending__marker--deleted">-</span>
            <span>{{ getPathLabel(entry.path) }}</span>
          </div>
        </div>
      </div>
    </div>

    <!-- Tree -->
    <div class="file-tree-content">
      <Tree
        v-model:selection-keys="selectedKeys"
        :value="treeNodes"
        selection-mode="multiple"
        class="w-full border-none p-0 file-tree-component"
        @node-select="onNodeSelect"
      >
        <template #default="{ node }">
          <div
            class="tree-node-content"
            :class="{
              'tree-node-active': isActive(node.data?.path),
              'tree-node-dirty': isDirty(node.data?.path),
              'tree-node-pending': getPendingChangeType(node.data?.path),
            }"
          >
            <span class="tree-node-label">{{ node.label }}</span>
            <div class="tree-node-actions">
              <span
                v-if="getPendingChangeType(node.data?.path)"
                class="change-indicator"
                :class="`change-indicator--${getPendingChangeType(node.data?.path)}`"
              >
                {{ getChangeMarker(getPendingChangeType(node.data?.path)) }}
              </span>
              <span v-if="isDirty(node.data?.path)" class="dirty-indicator">*</span>
              <Button
                v-if="node.data?.type === 'file' && canDeleteFile(node.data?.path)"
                v-tooltip.left="'Delete file'"
                icon="pi pi-trash"
                text
                rounded
                size="small"
                severity="secondary"
                class="delete-file-button"
                @click.stop="emit('deleteFile', node.data.path)"
              />
              <Button
                v-else-if="node.data?.type === 'folder' && canDeleteFolder(node.data?.path)"
                v-tooltip.left="'Delete folder'"
                icon="pi pi-trash"
                text
                rounded
                size="small"
                severity="secondary"
                class="delete-file-button"
                @click.stop="emit('deleteFolder', node.data.path)"
              />
            </div>
          </div>
        </template>
      </Tree>

      <!-- Empty state for files -->
      <div v-if="fileTree.length === 0" class="empty-state">
        <i class="pi pi-folder-open text-4xl text-color-secondary mb-2"></i>
        <p class="text-color-secondary text-sm">{{ emptyMessage }}</p>
        <Button
          v-if="showCreate"
          label="Create File"
          icon="pi pi-plus"
          size="small"
          outlined
          @click="emit('createFile')"
        />
      </div>
    </div>
  </div>
</template>

<style scoped>
.file-tree {
  height: 100%;
  display: flex;
  flex-direction: column;
}

.file-tree-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 0.75rem 1rem;
  border-bottom: 1px solid var(--p-surface-200);
  flex-shrink: 0;
}

.file-tree-content {
  flex: 1;
  overflow: hidden;
  display: flex;
  flex-direction: column;
  min-height: 0;
}

.file-tree-pending {
  padding: 0.65rem 1rem 0.75rem;
  border-bottom: 1px solid var(--p-surface-200);
}

.file-tree-pending__summary {
  width: 100%;
  display: inline-flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.5rem;
  border: none;
  background: transparent;
  padding: 0;
  color: var(--p-text-color);
  font-size: 0.9rem;
  font-weight: 600;
  text-align: left;
  cursor: pointer;
}

.file-tree-pending__details {
  display: flex;
  flex-direction: column;
  gap: 0.8rem;
  margin-top: 0.7rem;
}

.file-tree-pending__group {
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
}

.file-tree-pending__label {
  color: var(--p-text-muted-color);
  font-size: 0.75rem;
  font-weight: 700;
  letter-spacing: 0.04em;
  text-transform: uppercase;
}

.file-tree-pending__entry {
  display: flex;
  align-items: center;
  gap: 0.45rem;
  font-size: 0.85rem;
}

.file-tree-pending__marker {
  display: inline-flex;
  width: 0.9rem;
  justify-content: center;
  font-weight: 700;
}

.file-tree-pending__marker--modified {
  color: var(--p-orange-500);
}

.file-tree-pending__marker--added {
  color: var(--p-green-500);
}

.file-tree-pending__marker--deleted {
  color: var(--p-red-500);
}

.file-tree-component {
  flex: 1;
  overflow-y: auto;
  min-height: 0;
  padding: 0.5rem;
}

.file-tree-component :deep(.p-tree) {
  background: transparent;
  border: none;
  padding: 0;
  margin: 0;
}

.file-tree-component :deep(.p-tree-root) {
  overflow: visible !important;
}

.file-tree-component :deep(.p-tree-node-content) {
  padding: 0.25rem 0.5rem;
  border-radius: var(--p-border-radius);
}

.file-tree-component :deep(.p-tree-node-label) {
  flex: 1;
  min-width: 0;
}

.file-tree-component :deep(.p-tree-node-content:hover) {
  background-color: var(--p-surface-100);
}

.tree-node-content {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.5rem;
  padding: 0.25rem 0;
  width: 100%;
  min-width: 0;
}

.tree-node-label {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.tree-node-actions {
  display: flex;
  align-items: center;
  gap: 0.25rem;
  flex-shrink: 0;
  margin-left: auto;
}

.change-indicator {
  display: inline-flex;
  width: 0.9rem;
  justify-content: center;
  font-weight: 700;
}

.change-indicator--modified {
  color: var(--p-orange-500);
}

.change-indicator--added {
  color: var(--p-green-500);
}

.change-indicator--deleted {
  color: var(--p-red-500);
}

.delete-file-button {
  width: 1.65rem;
  height: 1.65rem;
  opacity: 0;
  pointer-events: none;
  color: var(--p-text-muted-color);
  transition: opacity 0.15s ease, background-color 0.15s ease;
}

.delete-file-button:hover,
.delete-file-button:focus-visible {
  color: var(--p-red-500);
}

.file-tree-component :deep(.p-tree-node-content:hover) .delete-file-button,
.file-tree-component :deep(.p-tree-node-content:focus-within) .delete-file-button,
.tree-node-active .delete-file-button {
  opacity: 1;
  pointer-events: auto;
}

.file-tree-header-actions {
  display: flex;
  align-items: center;
  gap: 0.25rem;
}

.tree-node-active {
  color: var(--p-primary-500);
  font-weight: 500;
}

.tree-node-dirty {
  font-style: italic;
}

.dirty-indicator {
  color: var(--p-orange-500);
  margin-left: 0.25rem;
  font-weight: bold;
}

@media (hover: none) {
  .delete-file-button {
    opacity: 1;
    pointer-events: auto;
  }
}

.empty-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 2rem;
  text-align: center;
}

/* Dark mode */
:global(.app-dark) .file-tree-header {
  border-color: var(--p-surface-700);
}

:global(.app-dark) .file-tree-pending {
  border-color: var(--p-surface-700);
}

:global(.app-dark) .file-tree-component :deep(.p-tree-node-content:hover) {
  background-color: var(--p-surface-800);
}
</style>
