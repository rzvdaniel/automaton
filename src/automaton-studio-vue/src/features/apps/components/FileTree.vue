<script setup lang="ts">
/**
 * FileTree.vue
 * File explorer component for the App Editor
 * Displays hierarchical file structure with icons and context menu
 */
import { ref, computed } from 'vue'
import Tree from 'primevue/tree'
import Button from 'primevue/button'
import Menu from 'primevue/menu'
import ContextMenu from 'primevue/contextmenu'
import { useAppEditorStore } from '../stores/app-editor.store'
import type { FileNode } from '../types/apps.types'

const emit = defineEmits<{
  createFile: []
  createFolder: []
  deleteFile: [path: string]
  renameFile: [path: string]
}>()

const editorStore = useAppEditorStore()

// Context menu reference
const contextMenu = ref<InstanceType<typeof ContextMenu> | null>(null)
const selectedContextNode = ref<FileNode | null>(null)

// Tree selection
const selectedKey = ref<Record<string, boolean>>({})

// Convert FileNode[] to PrimeVue Tree format
const treeNodes = computed(() => {
  return convertToTreeNodes(editorStore.fileTree)
})

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
  const fileNode = node.data as FileNode
  if (fileNode.type === 'file') {
    editorStore.openFile(fileNode.path)
  }
}

// Handle context menu
function onNodeContextMenu(event: any) {
  const node = event.node
  if (node) {
    selectedContextNode.value = node.data as FileNode
    contextMenu.value?.show(event.originalEvent)
  }
}

// Context menu items
const contextMenuItems = computed(() => {
  const node = selectedContextNode.value
  if (!node) return []

  const items: any[] = []

  if (node.type === 'file') {
    items.push({
      label: 'Open',
      icon: 'pi pi-folder-open',
      command: () => editorStore.openFile(node.path),
    })
    items.push({ separator: true })
    items.push({
      label: 'Delete',
      icon: 'pi pi-trash',
      class: 'text-red-500',
      command: () => emit('deleteFile', node.path),
    })
  } else {
    items.push({
      label: 'New File',
      icon: 'pi pi-file-plus',
      command: () => emit('createFile'),
    })
  }

  return items
})

// Check if a file is active
function isActive(path: string): boolean {
  return editorStore.activeFilePath === path
}

// Check if a file has unsaved changes
function isDirty(path: string): boolean {
  return editorStore.openFiles.some((f) => f.path === path && f.isDirty)
}
</script>

<template>
  <div class="file-tree">
    <!-- Header -->
    <div class="file-tree-header">
      <span class="font-semibold">Files</span>
      <Button
        icon="pi pi-plus"
        text
        rounded
        size="small"
        severity="secondary"
        @click="emit('createFile')"
        v-tooltip.bottom="'New File'"
      />
    </div>

    <!-- Tree -->
    <div class="file-tree-content">
      <Tree
        v-model:selectionKeys="selectedKey"
        :value="treeNodes"
        selectionMode="single"
        class="w-full border-none p-0 file-tree-component"
        @node-select="onNodeSelect"
        @node-context-menu="onNodeContextMenu"
      >
        <template #default="{ node }">
          <div
            class="tree-node-content"
            :class="{
              'tree-node-active': isActive(node.data?.path),
              'tree-node-dirty': isDirty(node.data?.path),
            }"
          >
            <i :class="node.icon" class="mr-2"></i>
            <span>{{ node.label }}</span>
            <span v-if="isDirty(node.data?.path)" class="dirty-indicator">*</span>
          </div>
        </template>
      </Tree>

      <!-- Empty state -->
      <div v-if="treeNodes.length === 0" class="empty-state">
        <i class="pi pi-folder-open text-4xl text-color-secondary mb-2"></i>
        <p class="text-color-secondary text-sm">No files yet</p>
        <Button
          label="Create File"
          icon="pi pi-plus"
          size="small"
          outlined
          @click="emit('createFile')"
        />
      </div>
    </div>

    <!-- Context Menu -->
    <ContextMenu ref="contextMenu" :model="contextMenuItems" />
  </div>
</template>

<style scoped>
.file-tree {
  height: 100%;
  display: flex;
  flex-direction: column;
  background-color: var(--p-surface-0);
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
  overflow-y: auto;
  padding: 0.5rem;
}

.file-tree-component :deep(.p-tree) {
  background: transparent;
  border: none;
  padding: 0;
}

.file-tree-component :deep(.p-tree-node-content) {
  padding: 0.25rem 0.5rem;
  border-radius: var(--p-border-radius);
}

.file-tree-component :deep(.p-tree-node-content:hover) {
  background-color: var(--p-surface-100);
}

.tree-node-content {
  display: flex;
  align-items: center;
  padding: 0.25rem 0;
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

.empty-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 2rem;
  text-align: center;
}

/* Dark mode */
:global(.app-dark) .file-tree {
  background-color: var(--p-surface-900);
}

:global(.app-dark) .file-tree-header {
  border-color: var(--p-surface-700);
}

:global(.app-dark) .file-tree-component :deep(.p-tree-node-content:hover) {
  background-color: var(--p-surface-800);
}
</style>
