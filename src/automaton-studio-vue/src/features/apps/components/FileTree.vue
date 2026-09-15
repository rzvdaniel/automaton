<script setup lang="ts">
import { computed, ref } from 'vue'
import Tree from 'primevue/tree'
import type { TreeNode } from 'primevue/treenode'
import type { FileNode } from '../types/apps.types'

const props = withDefaults(defineProps<{
  title?: string
  nodes: FileNode[]
  activePath?: string | null
  emptyMessage?: string
}>(), {
  title: 'Files',
  activePath: null,
  emptyMessage: 'No files available',
})

const emit = defineEmits<{
  openFile: [path: string]
}>()

const selectedKeys = ref<Record<string, boolean>>({})
const treeNodes = computed(() => convertToTreeNodes(props.nodes))

function convertToTreeNodes(nodes: FileNode[]): TreeNode[] {
  return nodes.map((node) => ({
    key: node.key,
    label: node.name,
    data: node,
    icon: node.type === 'folder' ? 'pi pi-folder' : getFileIcon(node.extension || ''),
    children: node.children ? convertToTreeNodes(node.children) : undefined,
    leaf: node.type === 'file',
  }))
}

function onNodeSelect(node: TreeNode) {
  const file = node.data
  if (file?.type === 'file' && typeof file.path === 'string') emit('openFile', file.path)
}

function getFileIcon(extension: string): string {
  switch (extension.toLowerCase()) {
    case 'css':
      return 'pi pi-palette'
    case 'js':
    case 'ts':
      return 'pi pi-code'
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
</script>

<template>
  <div class="file-tree">
    <div class="file-tree-header">
      <span class="font-semibold">{{ title }}</span>
      <slot name="header-actions" />
    </div>

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
            :class="{ 'tree-node-active': activePath === node.data?.path }"
          >
            <span class="tree-node-label">{{ node.label }}</span>
          </div>
        </template>
      </Tree>

      <div v-if="nodes.length === 0" class="empty-state">
        <i class="pi pi-folder-open text-4xl text-color-secondary mb-2"></i>
        <p class="text-color-secondary text-sm">{{ emptyMessage }}</p>
      </div>
    </div>
  </div>
</template>

<style scoped>
.file-tree {
  display: flex;
  flex-direction: column;
  height: 100%;
}

.file-tree-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0.75rem 1rem;
  border-bottom: 1px solid var(--p-surface-border);
}

.file-tree-content {
  flex: 1;
  overflow: auto;
}

.tree-node-content {
  width: 100%;
  min-width: 0;
  padding: 0.2rem 0.35rem;
  border-radius: 4px;
}

.tree-node-active {
  color: var(--p-primary-color);
  background: color-mix(in srgb, var(--p-primary-color) 12%, transparent);
}

.tree-node-label {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.empty-state {
  display: flex;
  align-items: center;
  justify-content: center;
  flex-direction: column;
  padding: 2rem 1rem;
  text-align: center;
}
</style>
