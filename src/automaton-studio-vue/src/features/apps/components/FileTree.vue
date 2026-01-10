<script setup lang="ts">
/**
 * FileTree.vue
 * File explorer component for the App Editor
 * Displays hierarchical file structure with icons and context menu
 * Includes a virtual "backend" folder for linked backend steps
 */
import { ref, computed } from 'vue'
import Tree from 'primevue/tree'
import Button from 'primevue/button'
import ContextMenu from 'primevue/contextmenu'
import { useConfirm } from 'primevue/useconfirm'
import { useToast } from 'primevue/usetoast'
import { useAppEditorStore } from '../stores/app-editor.store'
import type { FileNode, LinkedStepInfo } from '../types/apps.types'

const emit = defineEmits<{
  createFile: []
  createFolder: []
  deleteFile: [path: string]
  renameFile: [path: string]
  createStep: []
}>()

const confirm = useConfirm()
const toast = useToast()
const editorStore = useAppEditorStore()

// Context menu reference
const contextMenu = ref<InstanceType<typeof ContextMenu> | null>(null)
const selectedContextNode = ref<FileNode | null>(null)
const selectedContextStep = ref<LinkedStepInfo | null>(null)

// Tree selection
const selectedKey = ref<Record<string, boolean>>({})

// Convert FileNode[] to PrimeVue Tree format
const treeNodes = computed(() => {
  const appFiles = convertToTreeNodes(editorStore.fileTree)

  // Add backend folder with steps
  if (editorStore.linkedSteps.length > 0) {
    const backendFolder = {
      key: 'backend',
      label: 'backend',
      data: {
        key: 'backend',
        name: 'backend',
        path: 'backend',
        type: 'folder',
        isBackendFolder: true,
      } as FileNode & { isBackendFolder: boolean },
      icon: 'pi pi-server',
      children: editorStore.linkedSteps.map((step) => ({
        key: `backend/${step.name}`,
        label: `${step.displayName || step.name}.${getStepExtension(step.language)}`,
        data: {
          ...step,
          isBackendStep: true,
          path: `backend/${step.name}.${getStepExtension(step.language)}`,
        },
        icon: getStepIcon(step.language),
        leaf: true,
      })),
      leaf: false,
    }
    appFiles.push(backendFolder)
  } else {
    // Show empty backend folder with "+" indication
    const backendFolder = {
      key: 'backend',
      label: 'backend',
      data: {
        key: 'backend',
        name: 'backend',
        path: 'backend',
        type: 'folder',
        isBackendFolder: true,
      } as FileNode & { isBackendFolder: boolean },
      icon: 'pi pi-server',
      children: [],
      leaf: false,
    }
    appFiles.push(backendFolder)
  }

  return appFiles
})

function getStepExtension(language: string): string {
  switch (language?.toLowerCase()) {
    case 'python':
      return 'py'
    case 'powershell':
      return 'ps1'
    case 'batch':
      return 'bat'
    default:
      return 'py'
  }
}

function getStepIcon(language: string): string {
  switch (language?.toLowerCase()) {
    case 'python':
      return 'pi pi-code'
    case 'powershell':
      return 'pi pi-terminal'
    case 'batch':
      return 'pi pi-desktop'
    default:
      return 'pi pi-file'
  }
}

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
  const data = node.data

  // Handle backend step
  if (data?.isBackendStep) {
    const step = data as LinkedStepInfo & { isBackendStep: boolean }
    editorStore.openBackendStep(step)
    return
  }

  // Handle regular file
  const fileNode = data as FileNode
  if (fileNode.type === 'file') {
    editorStore.openFile(fileNode.path)
  }
}

// Handle context menu
function onNodeContextMenu(event: any) {
  const node = event.node
  if (!node) return

  const data = node.data

  // Handle backend step context menu
  if (data?.isBackendStep) {
    selectedContextStep.value = data as LinkedStepInfo
    selectedContextNode.value = null
  } else if (data?.isBackendFolder) {
    // Backend folder context menu
    selectedContextNode.value = data as FileNode
    selectedContextStep.value = null
  } else {
    selectedContextNode.value = data as FileNode
    selectedContextStep.value = null
  }

  contextMenu.value?.show(event.originalEvent)
}

// Context menu items
const contextMenuItems = computed(() => {
  // Backend step context menu
  if (selectedContextStep.value) {
    const step = selectedContextStep.value
    return [
      {
        label: 'Open',
        icon: 'pi pi-folder-open',
        command: () => editorStore.openBackendStep(step),
      },
      {
        label: 'Copy API Code',
        icon: 'pi pi-copy',
        command: () => copyStepCode(step),
      },
      { separator: true },
      {
        label: 'Unlink from App',
        icon: 'pi pi-times',
        class: 'text-red-500',
        command: () => handleUnlinkStep(step),
      },
    ]
  }

  const node = selectedContextNode.value
  if (!node) return []

  // Backend folder context menu
  if ((node as any).isBackendFolder) {
    return [
      {
        label: 'New Backend Step',
        icon: 'pi pi-plus',
        command: () => emit('createStep'),
      },
    ]
  }

  // Regular file/folder context menu
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

// Copy step usage code
async function copyStepCode(step: LinkedStepInfo) {
  const code = `await automatonApi.executeStep('${step.name}', { inputs: {} });`
  try {
    await navigator.clipboard.writeText(code)
    toast.add({
      severity: 'success',
      summary: 'Copied',
      detail: 'API code copied to clipboard',
      life: 2000,
    })
  } catch {
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: 'Failed to copy code',
      life: 3000,
    })
  }
}

// Handle unlink step
function handleUnlinkStep(step: LinkedStepInfo) {
  confirm.require({
    message: `Unlink step "${step.displayName || step.name}" from this app?`,
    header: 'Confirm Unlink',
    icon: 'pi pi-exclamation-triangle',
    acceptLabel: 'Unlink',
    rejectLabel: 'Cancel',
    accept: async () => {
      try {
        await editorStore.unlinkStep(step.id)
        toast.add({
          severity: 'success',
          summary: 'Unlinked',
          detail: 'Step unlinked from app',
          life: 3000,
        })
      } catch (err: unknown) {
        toast.add({
          severity: 'error',
          summary: 'Error',
          detail: err instanceof Error ? err.message : 'Failed to unlink step',
          life: 5000,
        })
      }
    },
  })
}

// Check if a file is active
function isActive(path: string): boolean {
  return editorStore.activeFilePath === path
}

// Check if a file has unsaved changes
function isDirty(path: string): boolean {
  return editorStore.openFiles.some((f) => f.path === path && f.isDirty)
}

// Check if node is a backend step
function isBackendStep(node: any): boolean {
  return node?.data?.isBackendStep === true
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
              'tree-node-backend': isBackendStep(node),
            }"
          >
            <span>{{ node.label }}</span>
            <span v-if="isDirty(node.data?.path)" class="dirty-indicator">*</span>
            <span v-if="isBackendStep(node)" class="backend-badge">step</span>
          </div>
        </template>
      </Tree>

      <!-- Empty state for files -->
      <div v-if="editorStore.fileTree.length === 0" class="empty-state">
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
  overflow: hidden;
  display: flex;
  flex-direction: column;
  min-height: 0;
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

.tree-node-backend {
  color: var(--p-text-color);
}

.dirty-indicator {
  color: var(--p-orange-500);
  margin-left: 0.25rem;
  font-weight: bold;
}

.backend-badge {
  margin-left: 0.5rem;
  font-size: 0.6rem;
  padding: 0.1rem 0.3rem;
  background-color: var(--p-primary-100);
  color: var(--p-primary-700);
  border-radius: 4px;
  font-weight: 500;
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

:global(.app-dark) .backend-badge {
  background-color: var(--p-primary-900);
  color: var(--p-primary-300);
}
</style>
