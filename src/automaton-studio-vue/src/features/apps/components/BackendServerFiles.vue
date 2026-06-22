<script setup lang="ts">
import { computed, defineAsyncComponent, onMounted, ref } from 'vue'
import Button from 'primevue/button'
import Message from 'primevue/message'
import ProgressSpinner from 'primevue/progressspinner'
import { useConfirm } from 'primevue/useconfirm'
import { useToast } from 'primevue/usetoast'
import FileTree from './FileTree.vue'
import { appsService } from '../services/apps.service'
import {
  RunnerFileArea,
  type FileNode,
  type RunnerBackendFile,
  type RunnerBackendFileContent,
  type RunnerFileArea as RunnerFileAreaType,
} from '../types/apps.types'

const CodeEditor = defineAsyncComponent(() => import('./CodeEditor.vue'))

const props = defineProps<{
  appId: string
}>()

const confirm = useConfirm()
const toast = useToast()

const backendFiles = ref<RunnerBackendFile[]>([])
const dataFiles = ref<RunnerBackendFile[]>([])
const activeArea = ref<RunnerFileAreaType>(RunnerFileArea.Backend)
const selectedPath = ref<string | null>(null)
const selectedContent = ref<RunnerBackendFileContent | null>(null)
const isLoading = ref(false)
const isReading = ref(false)
const error = ref<string | null>(null)

const activeFiles = computed(() =>
  activeArea.value === RunnerFileArea.Backend ? backendFiles.value : dataFiles.value,
)
const totalLoadedFiles = computed(() => backendFiles.value.length + dataFiles.value.length)

const fileTree = computed<FileNode[]>(() =>
  appsService.buildFileTree(activeFiles.value.map((file) => file.path)),
)

const selectedFile = computed(() =>
  selectedPath.value ? activeFiles.value.find((file) => file.path === selectedPath.value) : null,
)

const editorContent = computed(() => selectedContent.value?.content ?? '')
const activeSectionTitle = computed(() =>
  activeArea.value === RunnerFileArea.Backend ? 'Backend Files' : 'Persistent Data',
)
const activeEmptyMessage = computed(() =>
  activeArea.value === RunnerFileArea.Backend
    ? 'No backend files found on the runner.'
    : 'No persistent data files found on the runner.',
)
const activeTabDescription = computed(() =>
  activeArea.value === RunnerFileArea.Backend
    ? 'Preview deployed backend runtime files on the connected runner.'
    : 'Preview persistent runner data such as media, generated output, or local databases.',
)

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
    const [backendResponse, dataResponse] = await Promise.all([
      appsService.listRunnerBackendFiles(props.appId, RunnerFileArea.Backend),
      appsService.listRunnerBackendFiles(props.appId, RunnerFileArea.Data),
    ])
    backendFiles.value = backendResponse.files
    dataFiles.value = dataResponse.files

    if (selectedPath.value && !activeFiles.value.some((file) => file.path === selectedPath.value)) {
      selectedPath.value = null
      selectedContent.value = null
    }
  } catch (err: unknown) {
    error.value = getErrorMessage(err, 'Failed to load runner files')
  } finally {
    isLoading.value = false
  }
}

function switchArea(area: RunnerFileAreaType) {
  if (activeArea.value === area) {
    return
  }

  activeArea.value = area
  selectedPath.value = null
  selectedContent.value = null
}

async function openFile(path: string) {
  const file = activeFiles.value.find((candidate) => candidate.path === path)
  if (!file) return

  selectedPath.value = file.path
  selectedContent.value = null

  if (!file.isText) {
    selectedContent.value = {
      success: true,
      area: activeArea.value,
      path: file.path,
      size: file.size,
      isText: false,
      error: 'Binary preview is not supported.',
    }
    return
  }

  isReading.value = true
  try {
    selectedContent.value = await appsService.readRunnerBackendFile(props.appId, file.path, activeArea.value)
  } catch (err: unknown) {
    selectedContent.value = {
      success: false,
      area: activeArea.value,
      path: file.path,
      size: file.size,
      isText: file.isText,
      error: getErrorMessage(err, 'Failed to read runner file'),
    }
  } finally {
    isReading.value = false
  }
}

function confirmDelete(path: string) {
  const file = activeFiles.value.find((candidate) => candidate.path === path)
  if (!file) return

  const areaLabel = activeArea.value === RunnerFileArea.Backend ? 'backend runtime' : 'persistent data'

  confirm.require({
    message: `Delete "${file.path}" from the connected runner? This only removes the runner copy.`,
    header: `Delete ${areaLabel === 'backend runtime' ? 'Backend File' : 'Data File'}`,
    icon: 'pi pi-exclamation-triangle',
    acceptClass: 'p-button-danger',
    acceptLabel: 'Delete',
    rejectLabel: 'Cancel',
    accept: async () => {
      try {
        await appsService.deleteRunnerBackendFile(props.appId, file.path, activeArea.value)
        toast.add({
          severity: 'success',
          summary: 'Deleted',
          detail: `Deleted "${file.path}" from runner ${areaLabel}`,
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
          detail: getErrorMessage(err, 'Failed to delete runner file'),
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

    <div v-if="isLoading && totalLoadedFiles === 0" class="backend-server-loading">
      <ProgressSpinner />
    </div>

    <div v-else class="backend-editor-layout">
      <aside class="backend-file-panel">
        <div class="runner-panel-header">
          <div>
            <div class="runner-panel-title">Runner Files</div>
            <p class="runner-panel-copy">{{ activeTabDescription }}</p>
          </div>

          <div class="runner-panel-actions">
            <Button
              :label="'Backend Files'"
              :severity="activeArea === RunnerFileArea.Backend ? 'contrast' : 'secondary'"
              :outlined="activeArea !== RunnerFileArea.Backend"
              size="small"
              @click="switchArea(RunnerFileArea.Backend)"
            />
            <Button
              :label="'Persistent Data'"
              :severity="activeArea === RunnerFileArea.Data ? 'contrast' : 'secondary'"
              :outlined="activeArea !== RunnerFileArea.Data"
              size="small"
              @click="switchArea(RunnerFileArea.Data)"
            />
          </div>
        </div>

        <FileTree
          :title="activeSectionTitle"
          :nodes="fileTree"
          :active-path="selectedPath"
          :show-create="false"
          :empty-message="activeEmptyMessage"
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
              <i
                :class="activeArea === RunnerFileArea.Backend ? 'pi pi-server mr-1' : 'pi pi-database mr-1'"
                style="font-size: 0.7rem;"
              ></i>
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
            <p class="text-color-secondary text-sm m-0">Select a runner file to preview.</p>
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
  display: flex;
  flex-direction: column;
}

.runner-panel-header {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  padding: 0.9rem 1rem 0.75rem;
  border-bottom: 1px solid var(--p-surface-200);
}

.runner-panel-title {
  font-size: 0.95rem;
  font-weight: 700;
}

.runner-panel-copy {
  margin: 0.35rem 0 0;
  font-size: 0.78rem;
  line-height: 1.35;
  color: var(--p-text-muted-color);
}

.runner-panel-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
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
:global(.app-dark) .backend-editor-tabs,
:global(.app-dark) .runner-panel-header {
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

@media (max-width: 900px) {
  .backend-editor-layout {
    grid-template-columns: 1fr;
    grid-template-rows: minmax(260px, auto) minmax(0, 1fr);
  }

  .backend-file-panel {
    border-right: none;
    border-bottom: 1px solid var(--p-surface-200);
  }
}
</style>
