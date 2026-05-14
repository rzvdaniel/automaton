<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import Button from 'primevue/button'
import Message from 'primevue/message'
import ProgressSpinner from 'primevue/progressspinner'
import { useConfirm } from 'primevue/useconfirm'
import { useToast } from 'primevue/usetoast'
import CodeEditor from './CodeEditor.vue'
import FileTree from './FileTree.vue'
import { appsService } from '../services/apps.service'
import type { FileNode, RunnerBackendFile, RunnerBackendFileContent } from '../types/apps.types'

const props = defineProps<{
  appId: string
}>()

const confirm = useConfirm()
const toast = useToast()

const files = ref<RunnerBackendFile[]>([])
const selectedPath = ref<string | null>(null)
const selectedContent = ref<RunnerBackendFileContent | null>(null)
const isLoading = ref(false)
const isReading = ref(false)
const error = ref<string | null>(null)

const fileTree = computed<FileNode[]>(() =>
  appsService.buildFileTree(files.value.map((file) => file.path)),
)

const selectedFile = computed(() =>
  selectedPath.value ? files.value.find((file) => file.path === selectedPath.value) : null,
)

const editorContent = computed(() => selectedContent.value?.content ?? '')

const editorLanguage = computed(() => {
  if (!selectedPath.value) return 'plaintext'

  const extension = selectedPath.value.split('.').pop()?.toLowerCase()
  switch (extension) {
    case 'py':
      return 'python'
    case 'js':
      return 'javascript'
    case 'ts':
      return 'typescript'
    case 'json':
      return 'json'
    case 'css':
      return 'css'
    case 'html':
    case 'htm':
      return 'html'
    case 'md':
      return 'markdown'
    case 'yml':
    case 'yaml':
      return 'yaml'
    case 'xml':
      return 'xml'
    default:
      return 'plaintext'
  }
})

function getErrorMessage(err: unknown, fallback: string): string {
  if (err && typeof err === 'object') {
    const axiosError = err as {
      response?: { data?: { error?: string; message?: string } }
      message?: string
    }
    return axiosError.response?.data?.error
      || axiosError.response?.data?.message
      || axiosError.message
      || fallback
  }

  return fallback
}

async function loadFiles() {
  isLoading.value = true
  error.value = null

  try {
    const response = await appsService.listRunnerBackendFiles(props.appId)
    files.value = response.files

    if (selectedPath.value && !files.value.some((file) => file.path === selectedPath.value)) {
      selectedPath.value = null
      selectedContent.value = null
    }
  } catch (err: unknown) {
    error.value = getErrorMessage(err, 'Failed to load backend server files')
  } finally {
    isLoading.value = false
  }
}

async function openFile(path: string) {
  const file = files.value.find((candidate) => candidate.path === path)
  if (!file) return

  selectedPath.value = file.path
  selectedContent.value = null

  if (!file.isText) {
    selectedContent.value = {
      success: true,
      path: file.path,
      size: file.size,
      isText: false,
      error: 'Binary preview is not supported.',
    }
    return
  }

  isReading.value = true
  try {
    selectedContent.value = await appsService.readRunnerBackendFile(props.appId, file.path)
  } catch (err: unknown) {
    selectedContent.value = {
      success: false,
      path: file.path,
      size: file.size,
      isText: file.isText,
      error: getErrorMessage(err, 'Failed to read backend server file'),
    }
  } finally {
    isReading.value = false
  }
}

function confirmDelete(path: string) {
  const file = files.value.find((candidate) => candidate.path === path)
  if (!file) return

  confirm.require({
    message: `Delete "${file.path}" from the connected runner? This only removes the runner copy.`,
    header: 'Delete Backend Server File',
    icon: 'pi pi-exclamation-triangle',
    acceptClass: 'p-button-danger',
    acceptLabel: 'Delete',
    rejectLabel: 'Cancel',
    accept: async () => {
      try {
        await appsService.deleteRunnerBackendFile(props.appId, file.path)
        toast.add({
          severity: 'success',
          summary: 'Deleted',
          detail: `Deleted "${file.path}" from the runner`,
          life: 3000,
        })

        if (selectedPath.value === file.path) {
          selectedPath.value = null
          selectedContent.value = null
        }

        await loadFiles()
      } catch (err: unknown) {
        toast.add({
          severity: 'error',
          summary: 'Delete Failed',
          detail: getErrorMessage(err, 'Failed to delete backend server file'),
          life: 5000,
        })
      }
    },
  })
}

function formatBytes(size: number): string {
  if (size < 1024) return `${size} B`
  if (size < 1024 * 1024) return `${(size / 1024).toFixed(1)} KB`
  return `${(size / (1024 * 1024)).toFixed(1)} MB`
}

onMounted(loadFiles)
</script>

<template>
  <div class="backend-server-files">
    <Message v-if="error" severity="error" :closable="false" class="m-3">
      {{ error }}
    </Message>

    <div v-if="isLoading && files.length === 0" class="backend-server-loading">
      <ProgressSpinner />
    </div>

    <div v-else class="backend-editor-layout">
      <aside class="backend-file-panel">
        <FileTree
          title="Backend Server"
          :nodes="fileTree"
          :active-path="selectedPath"
          :show-create="false"
          empty-message="No backend files found on the runner."
          @open-file="openFile"
          @delete-file="confirmDelete"
        >
          <template #header-actions>
            <Button
              v-tooltip.bottom="'Refresh'"
              icon="pi pi-refresh"
              text
              rounded
              size="small"
              severity="secondary"
              :loading="isLoading"
              @click="loadFiles"
            />
          </template>
        </FileTree>
      </aside>

      <section class="backend-editor-panel">
        <div v-if="selectedFile" class="backend-editor-tabs">
          <div class="editor-tab active">
            <span class="tab-name">
              <i class="pi pi-server mr-1" style="font-size: 0.7rem;"></i>
              {{ selectedFile.path }}
            </span>
            <span class="backend-file-size">{{ formatBytes(selectedFile.size) }}</span>
            <Button
              v-tooltip.left="'Delete from runner'"
              icon="pi pi-trash"
              text
              rounded
              size="small"
              severity="danger"
              class="tab-close"
              @click="confirmDelete(selectedFile.path)"
            />
          </div>
        </div>

        <div class="backend-editor-content">
          <div v-if="isReading" class="backend-server-loading">
            <ProgressSpinner />
          </div>

          <Message
            v-else-if="selectedContent?.error && !selectedContent.content"
            severity="warn"
            :closable="false"
            class="m-3"
          >
            {{ selectedContent.error }}
          </Message>

          <CodeEditor
            v-else-if="selectedPath"
            :key="selectedPath"
            :file-path="selectedPath"
            :content="editorContent"
            :language="editorLanguage"
            read-only
          />

          <div v-else class="backend-empty">
            <i class="pi pi-file text-4xl text-color-secondary mb-2"></i>
            <p class="text-color-secondary text-sm m-0">Select a runner backend file to preview.</p>
          </div>
        </div>
      </section>
    </div>
  </div>
</template>

<style scoped>
.backend-server-files {
  display: flex;
  flex-direction: column;
  height: 100%;
  min-height: 0;
  background: var(--p-surface-ground);
}

.backend-server-loading,
.backend-empty {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  min-height: 12rem;
  text-align: center;
}

.backend-editor-layout {
  display: grid;
  grid-template-columns: minmax(220px, 280px) minmax(0, 1fr);
  flex: 1;
  min-height: 0;
}

.backend-file-panel {
  min-height: 0;
  border-right: 1px solid var(--p-surface-200);
}

.backend-editor-panel {
  min-width: 0;
  min-height: 0;
  display: flex;
  flex-direction: column;
}

.backend-editor-tabs {
  display: flex;
  align-items: center;
  gap: 0.25rem;
  padding: 0.5rem 0.75rem 0;
  background: var(--p-surface-50);
  border-bottom: 1px solid var(--p-surface-200);
  flex-shrink: 0;
}

.editor-tab {
  display: flex;
  align-items: center;
  gap: 0.375rem;
  min-width: 0;
  max-width: 100%;
  padding: 0.5rem 0.625rem;
  border: 1px solid var(--p-surface-200);
  border-bottom-color: transparent;
  border-radius: 0.5rem 0.5rem 0 0;
  background: var(--p-surface-0);
  color: var(--p-text-color);
}

.tab-name {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-size: 0.8125rem;
  font-weight: 600;
}

.backend-file-size {
  color: var(--p-text-muted-color);
  font-size: 0.75rem;
  flex-shrink: 0;
}

.tab-close {
  flex-shrink: 0;
}

.backend-editor-content {
  flex: 1;
  min-height: 0;
  overflow: hidden;
}

:global(.app-dark) .backend-file-panel,
:global(.app-dark) .backend-editor-tabs {
  border-color: var(--p-surface-700);
}

:global(.app-dark) .backend-editor-tabs {
  background: var(--p-surface-900);
}

:global(.app-dark) .editor-tab {
  background: var(--p-surface-800);
  border-color: var(--p-surface-700);
  border-bottom-color: transparent;
}
</style>
