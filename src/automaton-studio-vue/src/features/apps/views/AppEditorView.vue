<script setup lang="ts">
/**
 * AppEditorView.vue
 * Main Visual IDE for editing apps
 * AI-first layout: Files | Editor | AI Assistant
 * Preview opens as full-screen overlay
 */
import { ref, computed, onMounted, onUnmounted, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Tag from 'primevue/tag'
import ProgressSpinner from 'primevue/progressspinner'
import Toast from 'primevue/toast'
import Splitter from 'primevue/splitter'
import SplitterPanel from 'primevue/splitterpanel'
import { useConfirm } from 'primevue/useconfirm'
import { useToast } from 'primevue/usetoast'

// Import components
import FileTree from '../components/FileTree.vue'
import CodeEditor from '../components/CodeEditor.vue'
import AiAssistant from '../components/AiAssistant.vue'
import PreviewOverlay from '../components/PreviewOverlay.vue'
import PublishDialog from '../components/PublishDialog.vue'
import CreateStepDialog from '../components/CreateStepDialog.vue'
import CreateFileDialog from '../components/CreateFileDialog.vue'
import StatusBar from '../components/StatusBar.vue'
import AppSettings from '../components/AppSettings.vue'
import ConsolePanel from '../components/ConsolePanel.vue'
import AppVersionHistory from '../components/AppVersionHistory.vue'
import RunnerSelector from '@/shared/components/ui/RunnerSelector.vue'

import { useAppEditorStore } from '../stores/app-editor.store'
import type { CreateAppStepRequest, PublishAppRequest } from '../types/apps.types'
import type { StepResultMessage } from '../types/console.types'

const route = useRoute()
const router = useRouter()
const confirm = useConfirm()
const toast = useToast()
const editorStore = useAppEditorStore()

// Get app ID from route
const appId = computed(() => route.params.id as string)

const resolvedAppId = computed(() => editorStore.currentApp?.id || appId.value)

// Get initial prompt from query param (from "Generate App" flow)
const initialPrompt = computed(() => route.query.prompt as string | undefined)

// Dialog visibility
const showPublishDialog = ref(false)
const showCreateStepDialog = ref(false)
const showCreateFileDialog = ref(false)

// App name editing
const isEditingName = ref(false)
const editingName = ref('')

// Runner selection for AI tool execution
const selectedRunnerId = ref<string | null>(null)

// Active view tab (code, settings, or console)
const activeTab = ref<'code' | 'settings' | 'console'>('code')

// Right panel mode (aiAssistant or versionHistory)
const rightPanelMode = ref<'aiAssistant' | 'versionHistory'>('aiAssistant')

// Ref to version history component for manual refresh
const versionHistoryRef = ref<any>(null)

// AI Assistant configuration
const aiSystemPrompt = `You are an AI assistant helping to build a web application. 
You can create and modify HTML, CSS, and JavaScript code.
When a runner is attached, you can also execute backend steps for data processing.`

const aiSuggestions = [
  'Add a login form',
  'Create a navigation bar',
  'Add a contact form',
  'Create a data table',
]

// Panel sizes (in percentages) - localStorage keys
const STORAGE_KEY = 'app-editor-panel-sizes'
const defaultSizes = [15, 60, 25] // 15% tree, 60% editor, 25% AI

// Load sizes from localStorage or use defaults
function loadPanelSizes(): number[] {
  try {
    const stored = localStorage.getItem(STORAGE_KEY)
    if (stored) {
      const sizes = JSON.parse(stored)
      if (Array.isArray(sizes) && sizes.length === 3 && sizes.every((s) => typeof s === 'number')) {
        return sizes
      }
    }
  } catch (e) {
    console.warn('Failed to load panel sizes from localStorage')
  }
  return defaultSizes
}

// Save sizes to localStorage
function savePanelSizes(sizes: number[]) {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(sizes))
  } catch (e) {
    console.warn('Failed to save panel sizes to localStorage')
  }
}

// Normalize sizes to always add up to 100%
function normalizeSizes(sizes: number[]): number[] {
  if (!Array.isArray(sizes) || sizes.length !== 3) {
    return defaultSizes
  }
  
  const total = sizes.reduce((sum, size) => sum + size, 0)
  if (total === 0) {
    return defaultSizes
  }
  
  // Normalize to 100% and round to avoid floating point precision issues
  return sizes.map((size) => Math.round((size / total) * 10000) / 100)
}

const panelSizes = ref<number[]>(normalizeSizes(loadPanelSizes()))

// Handle splitter resize
function handleSplitterResize(event: any) {
  if (event && event.sizes && Array.isArray(event.sizes)) {
    // Normalize sizes to ensure they always add up to 100%
    const normalized = normalizeSizes(event.sizes)
    panelSizes.value = normalized
    savePanelSizes(normalized)
  }
}

// Active tab in editor
const activeTabIndex = ref(0)

// Get file index by path
function getFileTabIndex(path: string): number {
  return editorStore.openFiles.findIndex((f) => f.path === path)
}

// Update active tab when active file changes
watch(
  () => editorStore.activeFilePath,
  (path) => {
    if (path) {
      const index = getFileTabIndex(path)
      if (index !== -1) {
        activeTabIndex.value = index
      }
    }
  },
)

// Update active file when tab changes
watch(activeTabIndex, (index) => {
  const file = editorStore.openFiles[index]
  if (file && editorStore.activeFilePath !== file.path) {
    editorStore.activeFilePath = file.path
  }
})

// Existing file paths for create file dialog
const existingFilePaths = computed(() => {
  const paths: string[] = []
  function collectPaths(nodes: typeof editorStore.fileTree) {
    for (const node of nodes) {
      if (node.type === 'file') {
        paths.push(node.path)
      }
      if (node.children) {
        collectPaths(node.children)
      }
    }
  }
  collectPaths(editorStore.fileTree)
  return paths
})

// Load app on mount
onMounted(async () => {
  if (appId.value) {
    try {
      await editorStore.loadApp(appId.value)
      // Load the execution runner ID from the app
      if (editorStore.currentApp?.executionRunnerId) {
        selectedRunnerId.value = editorStore.currentApp.executionRunnerId
      }
    } catch (err: unknown) {
      toast.add({
        severity: 'error',
        summary: 'Error',
        detail: err instanceof Error ? err.message : 'Failed to load app',
        life: 5000,
      })
      router.push('/apps')
    }
  }
})

// Watch for runner selection changes and save to backend
watch(selectedRunnerId, async (newRunnerId) => {
  if (editorStore.currentApp && newRunnerId !== editorStore.currentApp.executionRunnerId) {
    try {
      // Update the app's execution runner ID
      await editorStore.updateAppRunner(newRunnerId)
      toast.add({
        severity: 'success',
        summary: 'Runner Updated',
        detail: 'Execution runner has been updated',
        life: 3000,
      })
    } catch (err: unknown) {
      toast.add({
        severity: 'error',
        summary: 'Error',
        detail: err instanceof Error ? err.message : 'Failed to update runner',
        life: 5000,
      })
      // Revert on error
      if (editorStore.currentApp?.executionRunnerId) {
        selectedRunnerId.value = editorStore.currentApp.executionRunnerId
      }
    }
  }
})

// Cleanup on unmount
onUnmounted(() => {
  editorStore.reset()
})

// Warn before leaving with unsaved changes
function handleBeforeUnload(e: BeforeUnloadEvent) {
  if (editorStore.hasUnsavedChanges) {
    e.preventDefault()
    e.returnValue = 'You have unsaved changes. Are you sure you want to leave?'
  }
}

// Handle preview iframe postMessage for console logging
function handlePreviewMessage(event: MessageEvent) {
  // Check if this is a step result message from our preview iframe
  if (event.data?.type === 'automatonStepResult') {
    const message = event.data as StepResultMessage
    editorStore.addConsoleEntry({
      id: crypto.randomUUID(),
      timestamp: new Date(message.data.timestamp),
      type: 'step-execution',
      stepName: message.data.stepName,
      inputs: message.data.inputs,
      result: message.data.result,
      duration: message.data.duration,
      source: message.data.source,
    })
  }

  if (event.data?.type === 'previewResourceError') {
    const url = event.data?.data?.url as string | undefined
    const tagName = event.data?.data?.tagName as string | undefined
    if (url) {
      const label = tagName ? `${tagName.toUpperCase()} ` : ''
      editorStore.setPreviewWarning(`Missing preview asset: ${label}${url}`)
    } else {
      editorStore.setPreviewWarning('Missing preview asset: a referenced file could not be loaded')
    }
  }
}

// Add beforeunload listener, fullscreen class, and message listener
onMounted(() => {
  window.addEventListener('beforeunload', handleBeforeUnload)
  window.addEventListener('message', handlePreviewMessage)
  // Add class to app-main for fullscreen editor mode
  document.querySelector('.app-main')?.classList.add('app-main--fullscreen')
})

onUnmounted(() => {
  window.removeEventListener('beforeunload', handleBeforeUnload)
  window.removeEventListener('message', handlePreviewMessage)
  // Remove fullscreen class
  document.querySelector('.app-main')?.classList.remove('app-main--fullscreen')
})

// Handle back navigation
function handleBack() {
  if (editorStore.hasUnsavedChanges) {
    confirm.require({
      message: 'You have unsaved changes. Are you sure you want to leave?',
      header: 'Unsaved Changes',
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Leave',
      rejectLabel: 'Stay',
      accept: () => {
        router.push('/apps')
      },
    })
  } else {
    router.push('/apps')
  }
}

// Handle save all
async function handleSaveAll() {
  try {
    await editorStore.saveAllFiles()
    toast.add({
      severity: 'success',
      summary: 'Saved',
      detail: 'All files saved successfully',
      life: 3000,
    })
  } catch (err: unknown) {
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: err instanceof Error ? err.message : 'Failed to save files',
      life: 5000,
    })
  }
}

// Handle save current file
async function handleSaveFile() {
  if (!editorStore.activeFilePath) return

  try {
    await editorStore.saveFile(editorStore.activeFilePath)
    toast.add({
      severity: 'success',
      summary: 'Saved',
      detail: 'File saved successfully',
      life: 3000,
    })
  } catch (err: unknown) {
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: err instanceof Error ? err.message : 'Failed to save file',
      life: 5000,
    })
  }
}

// Handle close tab
function handleCloseTab(event: Event, path: string) {
  event.stopPropagation()

  const file = editorStore.openFiles.find((f) => f.path === path)
  if (file?.isDirty) {
    confirm.require({
      message: `Save changes to "${path}" before closing?`,
      header: 'Unsaved Changes',
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Save',
      rejectLabel: "Don't Save",
      accept: async () => {
        await editorStore.saveFile(path)
        editorStore.closeFile(path, true)
      },
      reject: () => {
        editorStore.closeFile(path, true)
      },
    })
  } else {
    editorStore.closeFile(path)
  }
}

// Handle publish
async function handlePublish(request: PublishAppRequest) {
  try {
    await editorStore.publishApp(request)
    showPublishDialog.value = false
    toast.add({
      severity: 'success',
      summary: 'Published',
      detail: `App published at /web/${request.publicUrl}`,
      life: 5000,
    })
  } catch (err: unknown) {
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: err instanceof Error ? err.message : 'Failed to publish app',
      life: 5000,
    })
  }
}

// Handle unpublish
async function handleUnpublish() {
  confirm.require({
    message: 'Are you sure you want to unpublish this app? It will no longer be accessible at the public URL.',
    header: 'Confirm Unpublish',
    icon: 'pi pi-exclamation-triangle',
    acceptLabel: 'Unpublish',
    rejectLabel: 'Cancel',
    accept: async () => {
      try {
        await editorStore.unpublishApp()
        toast.add({
          severity: 'success',
          summary: 'Unpublished',
          detail: 'App has been unpublished',
          life: 3000,
        })
      } catch (err: unknown) {
        toast.add({
          severity: 'error',
          summary: 'Error',
          detail: err instanceof Error ? err.message : 'Failed to unpublish app',
          life: 5000,
        })
      }
    },
  })
}

// Handle create step
async function handleCreateStep(request: CreateAppStepRequest) {
  try {
    await editorStore.createBackendStep(request)
    showCreateStepDialog.value = false
    toast.add({
      severity: 'success',
      summary: 'Created',
      detail: `Step "${request.name}" created and linked`,
      life: 3000,
    })
  } catch (err: unknown) {
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: err instanceof Error ? err.message : 'Failed to create step',
      life: 5000,
    })
  }
}

// Handle create file
async function handleCreateFile(path: string, content: string) {
  try {
    await editorStore.createFile(path, content)
    showCreateFileDialog.value = false
    toast.add({
      severity: 'success',
      summary: 'Created',
      detail: `File "${path}" created`,
      life: 3000,
    })
  } catch (err: unknown) {
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: err instanceof Error ? err.message : 'Failed to create file',
      life: 5000,
    })
  }
}

// Handle delete file
async function handleDeleteFile(path: string) {
  confirm.require({
    message: `Are you sure you want to delete "${path}"? This action cannot be undone.`,
    header: 'Confirm Delete',
    icon: 'pi pi-exclamation-triangle',
    acceptClass: 'p-button-danger',
    acceptLabel: 'Delete',
    rejectLabel: 'Cancel',
    accept: async () => {
      try {
        await editorStore.deleteFile(path)
        toast.add({
          severity: 'success',
          summary: 'Deleted',
          detail: `File "${path}" deleted`,
          life: 3000,
        })
      } catch (err: unknown) {
        toast.add({
          severity: 'error',
          summary: 'Error',
          detail: err instanceof Error ? err.message : 'Failed to delete file',
          life: 5000,
        })
      }
    },
  })
}

// Handle version created - refresh the history list
async function handleVersionCreated() {
  if (versionHistoryRef.value?.loadVersions) {
    await versionHistoryRef.value.loadVersions()
  }
}

// Handle version restored
async function handleVersionRestored(versionNumber: number) {
  try {
    // Reload app files from the restored version
    await editorStore.loadApp(appId.value)
    toast.add({
      severity: 'success',
      summary: 'Restored',
      detail: `App restored to version ${versionNumber}. Files have been reloaded.`,
      life: 5000,
    })
  } catch (err: unknown) {
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: err instanceof Error ? err.message : 'Failed to reload app after restoration',
      life: 5000,
    })
  }
}

// Watch for version history tab activation - reload versions when tab becomes visible
watch(rightPanelMode, async (mode) => {
  if (mode === 'versionHistory' && versionHistoryRef.value?.loadVersions) {
    await versionHistoryRef.value.loadVersions()
  }
})

// Start editing app name
function startEditingName() {
  if (editorStore.currentApp) {
    editingName.value = editorStore.currentApp.name
    isEditingName.value = true
  }
}

// Cancel editing name
function cancelEditingName() {
  isEditingName.value = false
  editingName.value = ''
}

// Save app name
function saveAppName() {
  // TODO: Implement app name update
  isEditingName.value = false
}

// Handle AI tool execution for real-time file updates
async function handleAiToolExecuted(toolName: string, args: any, result?: any) {
  console.log('[AppEditor] AI tool executed:', { toolName, args, result })

  // File modification tools that should trigger refresh
  const fileModificationTools = [
    'write_file',
    'save_app_code',
    'delete_file',
    'create_backend_step',
    'save_step_code',
  ]

  if (fileModificationTools.includes(toolName)) {
    console.log(`[AppEditor] File modification tool "${toolName}" detected, refreshing files...`)

    // Determine which files were affected
    const affectedFiles: string[] = []

    if (toolName === 'write_file' && args.path) {
      console.log(`[AppEditor] write_file detected for path: ${args.path}`)
      affectedFiles.push(args.path)
    } else if (toolName === 'save_app_code') {
      console.log('[AppEditor] save_app_code detected, affecting index.html')
      affectedFiles.push('index.html')
    } else if (toolName === 'delete_file' && args.path) {
      // File was deleted, just refresh tree
      await editorStore.refreshFileTree()
      return
    } else if (toolName === 'create_backend_step' || toolName === 'save_step_code') {
      // Backend step created/updated, refresh linked steps
      await editorStore.refreshLinkedSteps()
      return
    }

    // Refresh files after AI update
    await editorStore.refreshFilesAfterAiUpdate(affectedFiles)

    // Show toast notification
    if (affectedFiles.length > 0) {
      toast.add({
        severity: 'info',
        summary: 'Files Updated',
        detail: `AI updated: ${affectedFiles.join(', ')}`,
        life: 3000,
      })
    }
  }
}
</script>

<template>
  <div class="app-editor-view">
    <!-- Loading state -->
    <div v-if="editorStore.isLoading" class="loading-container">
      <ProgressSpinner />
      <p class="mt-3">Loading app...</p>
    </div>

    <!-- Main editor -->
    <template v-else-if="editorStore.currentApp">
      <!-- Header -->
      <div class="editor-header">
        <div class="header-left">
          <Button
            v-tooltip.bottom="'Back to Apps'"
            icon="pi pi-arrow-left"
            text
            rounded
            severity="secondary"
            @click="handleBack"
          />
          <div class="app-info">
            <div class="flex align-items-center gap-2">
              <i class="pi pi-globe" style="color: var(--p-primary-500)"></i>
              <span v-if="!isEditingName" class="app-name" @click="startEditingName">
                {{ editorStore.currentApp.name }}
              </span>
              <InputText
                v-else
                v-model="editingName"
                class="app-name-input"
                @blur="saveAppName"
                @keyup.enter="saveAppName"
                @keyup.escape="cancelEditingName"
              />
              <Tag
                v-if="editorStore.isPublished"
                value="Published"
                severity="success"
              />
              <Tag v-else value="Draft" severity="warn" />
            </div>
            <!-- Tab buttons for Code/Console/Settings -->
            <div class="view-tabs">
              <button
                class="view-tab"
                :class="{ active: activeTab === 'code' }"
                @click="activeTab = 'code'"
              >
                <i class="pi pi-code"></i>
                Code
              </button>
              <button
                class="view-tab"
                :class="{ active: activeTab === 'console' }"
                @click="activeTab = 'console'"
              >
                <i class="pi pi-terminal"></i>
                Console
                <span v-if="editorStore.consoleEntries?.length > 0" class="console-badge">
                  {{ editorStore.consoleEntries.length }}
                </span>
              </button>
              <button
                class="view-tab"
                :class="{ active: activeTab === 'settings' }"
                @click="activeTab = 'settings'"
              >
                <i class="pi pi-cog"></i>
                Settings
              </button>
            </div>
          </div>
        </div>

        <div class="header-right">
          <!-- Runner Selector (only in code view) -->
          <template v-if="activeTab === 'code'">
            <div class="flex align-items-center gap-2">
              <label class="text-sm text-color-secondary">Runner:</label>
              <RunnerSelector
                v-model="selectedRunnerId"
                :disabled="editorStore.isSaving"
              />
            </div>

            <Button
              v-tooltip.bottom="'Open full-screen preview'"
              icon="pi pi-eye"
              label="Preview"
              outlined
              severity="secondary"
              @click="editorStore.openPreviewOverlay()"
            />
            <Button
              v-if="editorStore.hasUnsavedChanges"
              label="Save All"
              icon="pi pi-save"
              :loading="editorStore.isSaving"
              @click="handleSaveAll"
            />
          </template>
          <Button
            v-if="editorStore.isPublished"
            label="Unpublish"
            icon="pi pi-cloud-download"
            outlined
            severity="secondary"
            @click="handleUnpublish"
          />
          <Button
            v-else
            label="Publish"
            icon="pi pi-cloud-upload"
            @click="showPublishDialog = true"
          />
        </div>
      </div>

      <!-- Settings View -->
      <div v-show="activeTab === 'settings'" class="settings-view">
        <AppSettings />
      </div>

      <!-- Console View -->
      <div v-show="activeTab === 'console'" class="console-view">
        <ConsolePanel />
      </div>

      <!-- Code Editor View - Main editor layout with resizable splitters -->
      <Splitter
        v-show="activeTab === 'code'"
        class="editor-layout"
        layout="horizontal"
        :gutter-size="8"
        @resizeend="handleSplitterResize"
      >
        <!-- Left Panel: File Tree -->
        <SplitterPanel :size="panelSizes[0]" :min-size="10" class="splitter-panel">
          <div class="file-panel">
            <FileTree
              @create-file="showCreateFileDialog = true"
              @delete-file="handleDeleteFile"
              @create-step="showCreateStepDialog = true"
            />
          </div>
        </SplitterPanel>

        <!-- Center Panel: Code Editor -->
        <SplitterPanel :size="panelSizes[1]" :min-size="30" class="splitter-panel">
          <div class="editor-panel">
            <!-- File Tabs -->
            <div class="editor-tabs-container">
              <div
                v-for="file in editorStore.openFiles"
                :key="file.path"
                class="editor-tab"
                :class="{ active: editorStore.activeFilePath === file.path }"
                @click="editorStore.activeFilePath = file.path"
              >
                <span class="tab-name">
                  <i v-if="file.isBackendStep" class="pi pi-server mr-1" style="font-size: 0.7rem;"></i>
                  {{ file.isBackendStep ? file.stepName : file.path }}
                  <span v-if="file.isDirty" class="dirty-indicator">*</span>
                </span>
                <Button
                  icon="pi pi-times"
                  text
                  rounded
                  size="small"
                  class="tab-close"
                  @click="(e: Event) => handleCloseTab(e, file.path)"
                />
              </div>
            </div>

            <!-- Editor Content -->
            <div class="editor-content">
              <CodeEditor
                v-if="editorStore.activeFilePath"
                :key="editorStore.activeFilePath"
                :file-path="editorStore.activeFilePath"
                @save="handleSaveFile"
              />
              <div v-else class="no-file-selected">
                <i class="pi pi-file text-4xl text-color-secondary mb-2"></i>
                <p>Select a file to edit</p>
                <Button
                  label="Create File"
                  icon="pi pi-plus"
                  size="small"
                  outlined
                  @click="showCreateFileDialog = true"
                />
              </div>
            </div>
          </div>
        </SplitterPanel>

        <!-- Right Panel: AI Assistant or Version History -->
        <SplitterPanel :size="panelSizes[2]" :min-size="15" class="splitter-panel">
          <div class="right-panel-container">
            <!-- Right panel header tabs -->
            <div class="right-panel-header">
              <button
                class="right-panel-tab"
                :class="{ active: rightPanelMode === 'aiAssistant' }"
                @click="rightPanelMode = 'aiAssistant'"
              >
                <i class="pi pi-sparkles"></i>
                <span>AI Assistant</span>
              </button>
              <button
                class="right-panel-tab"
                :class="{ active: rightPanelMode === 'versionHistory' }"
                @click="rightPanelMode = 'versionHistory'"
              >
                <i class="pi pi-history"></i>
                <span>History</span>
              </button>
            </div>

            <!-- Right panel content -->
            <div class="ai-sidebar">
              <AiAssistant
                v-show="rightPanelMode === 'aiAssistant'"
                resource-type="WebApp"
                :resource-id="resolvedAppId"
                :runner-id="selectedRunnerId"
                :initial-prompt="initialPrompt"
                :system-prompt="aiSystemPrompt"
                :suggestions="aiSuggestions"
                @tool-executed="handleAiToolExecuted"
                @session-created="handleVersionCreated"
              />
              <AppVersionHistory
                v-show="rightPanelMode === 'versionHistory'"
                ref="versionHistoryRef"
                :app-id="resolvedAppId"
                @version-restored="handleVersionRestored"
                @version-created="handleVersionCreated"
              />
            </div>
          </div>
        </SplitterPanel>
      </Splitter>

      <!-- Status Bar -->
      <StatusBar />

      <!-- Full-screen Preview Overlay -->
      <PreviewOverlay v-if="editorStore.showPreviewOverlay" />
    </template>

    <!-- Error state -->
    <div v-else class="error-container">
      <i class="pi pi-exclamation-triangle text-5xl text-yellow-500 mb-3"></i>
      <p v-if="editorStore.error">{{ editorStore.error }}</p>
      <p v-else>App not found</p>
      <Button label="Back to Apps" icon="pi pi-arrow-left" @click="router.push('/apps')" />
    </div>

    <!-- Dialogs -->
    <PublishDialog
      v-model:visible="showPublishDialog"
      :current-slug="editorStore.currentApp?.publicUrl"
      :requires-auth="editorStore.currentApp?.requiresAuthentication"
      :app-name="editorStore.currentApp?.name"
      @publish="handlePublish"
    />

    <CreateStepDialog
      v-model:visible="showCreateStepDialog"
      @create="handleCreateStep"
    />

    <CreateFileDialog
      v-model:visible="showCreateFileDialog"
      :existing-files="existingFilePaths"
      @create="handleCreateFile"
    />

    <Toast />
  </div>
</template>

<style scoped>

.app-editor-view {
  display: flex;
  flex-direction: column;
  height: calc(100vh - 64px);
  background-color: var(--p-surface-ground);
  overflow: hidden;
}

.loading-container,
.error-container {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  height: 100%;
}

.editor-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 0.75rem 1rem;
  border-bottom: 1px solid var(--p-surface-200);
  flex-shrink: 0;
}

.header-left {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.app-info {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

/* View tabs (Code/Settings) */
.view-tabs {
  display: flex;
  gap: 0.25rem;
}

.view-tab {
  display: flex;
  align-items: center;
  gap: 0.375rem;
  padding: 0.375rem 0.75rem;
  border: none;
  background: transparent;
  color: var(--p-text-muted-color);
  font-size: 0.8125rem;
  font-weight: 500;
  cursor: pointer;
  border-radius: var(--p-border-radius);
  transition: all 0.15s ease;
}

.view-tab:hover {
  background: var(--p-surface-100);
  color: var(--p-text-color);
}

.view-tab.active {
  background: var(--p-primary-100);
  color: var(--p-primary-600);
}

.view-tab i {
  font-size: 0.75rem;
}

/* Right panel tabs */
.right-panel-container {
  display: flex;
  flex-direction: column;
  height: 100%;
  min-width: 0;
}

.right-panel-header {
  display: flex;
  gap: 0;
  border-bottom: 1px solid var(--p-surface-200);
  flex-shrink: 0;
}

.right-panel-tab {
  display: flex;
  align-items: center;
  gap: 0.375rem;
  padding: 0.75rem 1rem;
  border: none;
  background: transparent;
  color: var(--p-text-muted-color);
  font-size: 0.8125rem;
  font-weight: 500;
  cursor: pointer;
  border-radius: 0;
  border-bottom: 2px solid transparent;
  transition: all 0.15s ease;
}

.right-panel-tab:hover {
  background: var(--p-surface-100);
  color: var(--p-text-color);
}

.right-panel-tab.active {
  border-bottom-color: var(--p-primary-500);
  color: var(--p-primary-500);
}

.right-panel-tab i {
  font-size: 0.8rem;
}

.ai-sidebar {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  display: flex;
  flex-direction: column;
}

/* Settings view container */
.settings-view {
  flex: 1;
  overflow: hidden;
}

/* Console view container */
.console-view {
  flex: 1;
  overflow: hidden;
}

/* Console badge in tab */
.console-badge {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 1.25rem;
  height: 1.25rem;
  padding: 0 0.375rem;
  margin-left: 0.375rem;
  font-size: 0.6875rem;
  font-weight: 600;
  background: var(--p-primary-500);
  color: white;
  border-radius: 0.625rem;
}

.app-name {
  font-size: 1.125rem;
  font-weight: 600;
  cursor: pointer;
  color: var(--p-text-color);
}

.app-name:hover {
  text-decoration: underline;
}

.app-name-input {
  font-size: 1.125rem;
  font-weight: 600;
  width: 200px;
}

.header-right {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.editor-layout {
  display: flex;
  flex: 1;
  min-height: 0;
  overflow: hidden;
}

/* Splitter panel styles */
.splitter-panel {
  display: flex !important;
  flex-direction: column !important;
  min-width: 0 !important;
  overflow: hidden !important;
}

.file-panel {
  width: 100%;
  height: 100%;
  display: flex;
  flex-direction: column;
  border-right: 1px solid var(--p-surface-200);
}

.editor-panel {
  width: 100%;
  height: 100%;
  display: flex;
  flex-direction: column;
  min-width: 0;
}

.editor-tabs-container {
  display: flex;
  align-items: center;
  border-bottom: 1px solid var(--p-surface-200);
  overflow-x: auto;
  flex-shrink: 0;
}

.editor-tab {
  display: flex;
  align-items: center;
  gap: 0.25rem;
  padding: 0.5rem 0.75rem;
  border-right: 1px solid var(--p-surface-200);
  cursor: pointer;
  white-space: nowrap;
  transition: background-color 0.2s;
}

.editor-tab:hover {
  background-color: var(--p-surface-hover);
}

.editor-tab.active {
  border-bottom: 2px solid var(--p-primary-500);
}

.tab-name {
  font-size: 0.875rem;
}

.dirty-indicator {
  color: var(--p-orange-500);
  margin-left: 0.25rem;
}

.tab-close {
  opacity: 0.5;
}

.tab-close:hover {
  opacity: 1;
}

.editor-content {
  flex: 1;
  min-height: 0;
  overflow: hidden;
}

.no-file-selected {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  height: 100%;
  color: var(--p-text-secondary);
}

.ai-sidebar {
  width: 100%;
  height: 100%;
  display: flex;
  flex-direction: column;
  border-left: 1px solid var(--p-surface-200);
}

/* Dark mode */
:global(.app-dark) .app-editor-view {
  background-color: var(--p-surface-900);
}

:global(.app-dark) .editor-header {
  border-color: var(--p-surface-700);
}

:global(.app-dark) .file-panel {
  border-color: var(--p-surface-700);
}

:global(.app-dark) .editor-tabs-container {
  border-color: var(--p-surface-700);
}

:global(.app-dark) .editor-tab {
  border-color: var(--p-surface-700);
}

:global(.app-dark) .ai-sidebar {
  border-color: var(--p-surface-700);
}

:global(.app-dark) .view-tab:hover {
  background: var(--p-surface-700);
}

:global(.app-dark) .view-tab.active {
  background: var(--p-primary-900);
  color: var(--p-primary-300);
}

/* Splitter gutter styling */
:global(.p-splitter-gutter) {
  background-color: var(--p-surface-200);
  transition: background-color 0.2s ease;
}

:global(.p-splitter-gutter:hover) {
  background-color: var(--p-primary-300);
}

:global(.app-dark) .p-splitter-gutter {
  background-color: var(--p-surface-700);
}

:global(.app-dark) .p-splitter-gutter:hover {
  background-color: var(--p-primary-600);
}
</style>
