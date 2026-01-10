# Phase 3.3b - App Editor (Visual IDE) Implementation

**AI Agent Prompt for App Editor Visual IDE Implementation**

---

## Context Summary

Apps in Automaton are **full-stack web applications** consisting of:
- **Frontend**: HTML/CSS/JavaScript files (ES6 supported)
- **Backend**: Custom Steps (Python/PowerShell/Batch) that execute on Runners
- **Storage**: File system (`/AppData/apps/{UserId}/{AppId}/`) with database metadata
- **AI Generation**: MCP client with 8 tools for full-stack development
- **Communication**: `automatonApi.js` injected for calling backend steps from frontend

The App Editor is a Visual IDE with:
- Multi-file code editor (Monaco Editor)
- File tree navigation
- Live preview in iframe
- Backend step management
- AI assistant for code generation
- Publish workflow

---

## Prompt for AI Agent

```
Implement Phase 3.3b - App Editor (Visual IDE) from REDESIGN_PLAN.md.

PREREQUISITE: Before starting, READ THE ENTIRE src/automaton-studio-vue/REDESIGN_PLAN.md file.
Pay special attention to:
- Section 6: Project Structure (Feature-Based) - work in features/apps/ structure
- Section 7.4: App Editor design spec - follow exact layout
- Common Pitfalls for AI Agents - avoid these mistakes!
- Phase 1.2 completed: App Editor stub already exists, you're adding functionality
- Phase 3.3a completed: Apps List page works (navigation from list to editor)

IMPORTANT BACKGROUND - READ CAREFULLY:

Apps are full-stack web applications with:
1. **Frontend Files**: HTML/CSS/JavaScript stored in file system
2. **Backend Steps**: Python/PowerShell/Batch code that runs on Runners
3. **AI Generation**: MCP client generates both frontend and backend code
4. **AutomatonAPI**: JavaScript library injected into apps for calling backend steps
5. **Preview/Publish**: Test apps before publishing to public URLs

Review Blazor implementation for architecture:
- src/Automaton.Studio/Pages/Apps/AppPage.razor (main editor UI)
- src/Automaton.Studio/Pages/Apps/AppPage.razor.cs (logic, file management, AI)
- src/Automaton.Studio.Server/Controllers/AppsController.cs (API endpoints)
- src/Automaton.Studio.Server/Services/AppsService.cs (app CRUD)
- src/Automaton.Studio.Server/Services/AppFileStorageService.cs (file I/O)
- src/Automaton.Studio.Server/Services/AppStepsService.cs (step-app linking)
- src/Automaton.Studio/Services/Mcp/ToolProviders/AppMcpToolProvider.cs (AI tools)
- src/Automaton.Studio/wwwroot/automatonApi.js (frontend API client)

Key Implementation Tasks:

1. App Editor Feature Structure (ADD to existing):
   - features/apps/types/apps.types.ts (EXTEND from Phase 3.3a)
   - features/apps/services/apps.service.ts (EXTEND)
   - features/apps/services/app-files.service.ts (NEW - file operations)
   - features/apps/stores/app-editor.store.ts (NEW - editor state)
   - features/apps/components/FileTree.vue (NEW)
   - features/apps/components/CodeEditor.vue (NEW - Monaco integration)
   - features/apps/components/PreviewFrame.vue (NEW - iframe preview)
   - features/apps/components/StepsPanel.vue (NEW - backend steps list)
   - features/apps/components/AiAssistant.vue (NEW - AI chat for app generation)
   - features/apps/views/AppEditorView.vue (EXISTS - UPDATE from stub)

2. Extended TypeScript Interfaces (apps.types.ts):
   ```typescript
   // File tree node
   export interface FileNode {
     name: string
     path: string
     type: 'file' | 'folder'
     extension?: string
     children?: FileNode[]
     isOpen?: boolean
   }

   // App file content
   export interface AppFile {
     path: string
     content: string
     language: string  // 'html', 'css', 'javascript', 'json'
     isDirty: boolean  // Has unsaved changes
   }

   // App definition (complete structure)
   export interface AppDefinition {
     htmlContent: string  // Main index.html
     files: Record<string, string>  // Additional files (path -> content)
     metadata: AppMetadata
   }

   export interface AppMetadata {
     version: string
     lastModified: string
     flowIds: string[]  // Linked flow IDs
     githubRepo?: string
     githubBranch?: string
   }

   // Backend step info (linked to app)
   export interface LinkedStepInfo {
     id: string
     name: string
     displayName: string
     language: 'Python' | 'PowerShell' | 'Batch'
     description: string
     inputCount: number
     outputCount: number
     inputVariables: StepVariable[]
     outputVariables: StepVariable[]
   }

   export interface StepVariable {
     name: string
     description: string
     isSensitive: boolean
   }

   // App execution context
   export interface AppExecutionContext {
     appId: string
     slug?: string  // Only if published
     requiresAuthentication: boolean
     executionRunnerId?: string
   }

   // Publish request
   export interface PublishAppRequest {
     publicUrl: string  // Desired slug
     requiresAuthentication: boolean
     executionRunnerId?: string
   }
   ```

3. App Files Service (app-files.service.ts):
   ```typescript
   import axios from '@/shared/services/api/axios-instance'
   import type { AppFile, FileNode } from '../types/apps.types'

   export const appFilesService = {
     // Get file tree structure
     async getFileTree(appId: string): Promise<FileNode[]> {
       const response = await axios.get(`/api/apps/${appId}/files`)
       return response.data
     },

     // Get file content
     async getFileContent(appId: string, filePath: string): Promise<string> {
       const response = await axios.get(`/api/apps/${appId}/files/${encodeURIComponent(filePath)}`)
       return response.data
     },

     // Save file content
     async saveFile(appId: string, filePath: string, content: string): Promise<void> {
       await axios.put(`/api/apps/${appId}/files/${encodeURIComponent(filePath)}`, {
         content
       })
     },

     // Create new file
     async createFile(appId: string, filePath: string, content: string = ''): Promise<void> {
       await axios.post(`/api/apps/${appId}/files`, {
         path: filePath,
         content
       })
     },

     // Delete file
     async deleteFile(appId: string, filePath: string): Promise<void> {
       await axios.delete(`/api/apps/${appId}/files/${encodeURIComponent(filePath)}`)
     },

     // Rename file
     async renameFile(appId: string, oldPath: string, newPath: string): Promise<void> {
       await axios.post(`/api/apps/${appId}/files/rename`, {
         oldPath,
         newPath
       })
     },

     // Get app definition (all files + metadata)
     async getAppDefinition(appId: string): Promise<AppDefinition> {
       const response = await axios.get(`/api/apps/${appId}/definition`)
       return response.data
     },

     // Save app definition (batch update)
     async saveAppDefinition(appId: string, definition: AppDefinition): Promise<void> {
       await axios.put(`/api/apps/${appId}/definition`, definition)
     }
   }

   // Backend steps service (separate file or extend apps.service.ts)
   export const appStepsService = {
     // Get linked steps
     async getLinkedSteps(appId: string): Promise<LinkedStepInfo[]> {
       const response = await axios.get(`/api/apps/${appId}/steps`)
       return response.data
     },

     // Create and link new step
     async createStep(appId: string, name: string, language: string, description: string): Promise<LinkedStepInfo> {
       const response = await axios.post(`/api/apps/${appId}/steps`, {
         name,
         language,
         description
       })
       return response.data
     },

     // Link existing step
     async linkStep(appId: string, stepId: string): Promise<void> {
       await axios.post(`/api/apps/${appId}/steps/${stepId}/link`)
     },

     // Unlink step
     async unlinkStep(appId: string, stepId: string): Promise<void> {
       await axios.delete(`/api/apps/${appId}/steps/${stepId}`)
     },

     // Get step code (for viewing in dialog)
     async getStepCode(stepId: string): Promise<string> {
       const response = await axios.get(`/api/custom-steps/${stepId}/code`)
       return response.data
     }
   }
   ```

4. Extend Apps Service (apps.service.ts from Phase 3.3a):
   ```typescript
   // Add to existing appsService
   export const appsService = {
     // ... existing methods from Phase 3.3a ...

     // Get preview HTML (with automatonApi.js injected)
     async getPreviewHtml(appId: string): Promise<string> {
       const response = await axios.get(`/api/apps/${appId}/preview`)
       return response.data
     },

     // Publish app
     async publish(id: string, request: PublishAppRequest): Promise<App> {
       const response = await axios.post(`/api/apps/${id}/publish`, request)
       return response.data
     },

     // Unpublish app
     async unpublish(id: string): Promise<App> {
       const response = await axios.post(`/api/apps/${id}/unpublish`)
       return response.data
     },

     // Validate slug availability
     async validateSlug(slug: string, excludeAppId?: string): Promise<boolean> {
       const response = await axios.get(`/api/apps/validate-slug/${slug}`, {
         params: { excludeAppId }
       })
       return response.data.available
     }
   }
   ```

5. App Editor Store (app-editor.store.ts - Pinia):
   ```typescript
   import { defineStore } from 'pinia'
   import { appsService } from '../services/apps.service'
   import { appFilesService, appStepsService } from '../services/app-files.service'
   import type { App, AppFile, FileNode, LinkedStepInfo, AppDefinition } from '../types/apps.types'

   export const useAppEditorStore = defineStore('appEditor', {
     state: () => ({
       currentApp: null as App | null,
       appDefinition: null as AppDefinition | null,
       fileTree: [] as FileNode[],
       openFiles: [] as AppFile[],
       activeFilePath: null as string | null,
       linkedSteps: [] as LinkedStepInfo[],
       previewHtml: '',
       loading: false,
       saving: false,
       error: null as string | null
     }),

     getters: {
       activeFile: (state) => {
         if (!state.activeFilePath) return null
         return state.openFiles.find(f => f.path === state.activeFilePath) || null
       },

       hasUnsavedChanges: (state) => {
         return state.openFiles.some(f => f.isDirty)
       },

       mainHtmlFile: (state) => {
         return state.openFiles.find(f => f.path === 'index.html')
       }
     },

     actions: {
       async loadApp(appId: string) {
         this.loading = true
         this.error = null
         try {
           // Load app metadata
           this.currentApp = await appsService.get(appId)

           // Load app definition (files + metadata)
           this.appDefinition = await appFilesService.getAppDefinition(appId)

           // Load file tree
           this.fileTree = await appFilesService.getFileTree(appId)

           // Load linked backend steps
           this.linkedSteps = await appStepsService.getLinkedSteps(appId)

           // Open index.html by default
           if (this.appDefinition.htmlContent) {
             this.openFiles.push({
               path: 'index.html',
               content: this.appDefinition.htmlContent,
               language: 'html',
               isDirty: false
             })
             this.activeFilePath = 'index.html'
           }

           // Load preview HTML
           await this.refreshPreview()
         } catch (err: any) {
           this.error = err.response?.data?.message || 'Failed to load app'
           throw err
         } finally {
           this.loading = false
         }
       },

       async openFile(filePath: string) {
         // Check if already open
         const existing = this.openFiles.find(f => f.path === filePath)
         if (existing) {
           this.activeFilePath = filePath
           return
         }

         try {
           // Load file content from API
           const content = await appFilesService.getFileContent(this.currentApp!.id, filePath)

           // Determine language from extension
           const ext = filePath.split('.').pop()?.toLowerCase()
           const language = ext === 'html' ? 'html' :
                          ext === 'css' ? 'css' :
                          ext === 'js' ? 'javascript' :
                          ext === 'json' ? 'json' : 'plaintext'

           this.openFiles.push({
             path: filePath,
             content,
             language,
             isDirty: false
           })

           this.activeFilePath = filePath
         } catch (err: any) {
           throw new Error(`Failed to open file: ${err.message}`)
         }
       },

       closeFile(filePath: string) {
         const index = this.openFiles.findIndex(f => f.path === filePath)
         if (index === -1) return

         // Check for unsaved changes
         const file = this.openFiles[index]
         if (file.isDirty) {
           // Should prompt user in UI before calling this
           throw new Error('File has unsaved changes')
         }

         this.openFiles.splice(index, 1)

         // If closing active file, switch to another
         if (this.activeFilePath === filePath) {
           this.activeFilePath = this.openFiles.length > 0 ? this.openFiles[0].path : null
         }
       },

       updateFileContent(filePath: string, content: string) {
         const file = this.openFiles.find(f => f.path === filePath)
         if (file) {
           file.content = content
           file.isDirty = true
         }
       },

       async saveFile(filePath: string) {
         const file = this.openFiles.find(f => f.path === filePath)
         if (!file) return

         try {
           await appFilesService.saveFile(this.currentApp!.id, filePath, file.content)
           file.isDirty = false

           // If index.html changed, refresh preview
           if (filePath === 'index.html') {
             await this.refreshPreview()
           }
         } catch (err: any) {
           throw new Error(`Failed to save file: ${err.message}`)
         }
       },

       async saveAllFiles() {
         this.saving = true
         try {
           const dirtyFiles = this.openFiles.filter(f => f.isDirty)

           for (const file of dirtyFiles) {
             await appFilesService.saveFile(this.currentApp!.id, file.path, file.content)
             file.isDirty = false
           }

           // Refresh preview after saving all
           await this.refreshPreview()
         } catch (err: any) {
           throw new Error(`Failed to save files: ${err.message}`)
         } finally {
           this.saving = false
         }
       },

       async createFile(filePath: string, content: string = '') {
         try {
           await appFilesService.createFile(this.currentApp!.id, filePath, content)

           // Refresh file tree
           this.fileTree = await appFilesService.getFileTree(this.currentApp!.id)

           // Open the new file
           await this.openFile(filePath)
         } catch (err: any) {
           throw new Error(`Failed to create file: ${err.message}`)
         }
       },

       async deleteFile(filePath: string) {
         try {
           await appFilesService.deleteFile(this.currentApp!.id, filePath)

           // Close if open
           const index = this.openFiles.findIndex(f => f.path === filePath)
           if (index !== -1) {
             this.openFiles.splice(index, 1)
           }

           // Refresh file tree
           this.fileTree = await appFilesService.getFileTree(this.currentApp!.id)
         } catch (err: any) {
           throw new Error(`Failed to delete file: ${err.message}`)
         }
       },

       async refreshPreview() {
         if (!this.currentApp) return

         try {
           this.previewHtml = await appsService.getPreviewHtml(this.currentApp.id)
         } catch (err: any) {
           console.error('Failed to refresh preview:', err)
         }
       },

       async publishApp(publicUrl: string, requiresAuthentication: boolean, executionRunnerId?: string) {
         if (!this.currentApp) return

         try {
           // Save all files first
           if (this.hasUnsavedChanges) {
             await this.saveAllFiles()
           }

           const updated = await appsService.publish(this.currentApp.id, {
             publicUrl,
             requiresAuthentication,
             executionRunnerId
           })

           this.currentApp = updated
         } catch (err: any) {
           throw new Error(`Failed to publish app: ${err.message}`)
         }
       },

       async unpublishApp() {
         if (!this.currentApp) return

         try {
           const updated = await appsService.unpublish(this.currentApp.id)
           this.currentApp = updated
         } catch (err: any) {
           throw new Error(`Failed to unpublish app: ${err.message}`)
         }
       },

       // Backend Steps Management
       async refreshLinkedSteps() {
         if (!this.currentApp) return

         try {
           this.linkedSteps = await appStepsService.getLinkedSteps(this.currentApp.id)
         } catch (err: any) {
           console.error('Failed to refresh linked steps:', err)
         }
       },

       async createBackendStep(name: string, language: string, description: string) {
         if (!this.currentApp) return

         try {
           const step = await appStepsService.createStep(this.currentApp.id, name, language, description)
           this.linkedSteps.push(step)
           return step
         } catch (err: any) {
           throw new Error(`Failed to create step: ${err.message}`)
         }
       },

       async unlinkStep(stepId: string) {
         if (!this.currentApp) return

         try {
           await appStepsService.unlinkStep(this.currentApp.id, stepId)
           this.linkedSteps = this.linkedSteps.filter(s => s.id !== stepId)
         } catch (err: any) {
           throw new Error(`Failed to unlink step: ${err.message}`)
         }
       }
     }
   })
   ```

6. Monaco Editor Component (CodeEditor.vue):

   Install Monaco Editor first:
   ```bash
   npm install monaco-editor
   npm install @monaco-editor/loader
   npm install -D monaco-editor-webpack-plugin  # If using webpack
   ```

   Component implementation:
   ```vue
   <script setup lang="ts">
   import { ref, onMounted, onUnmounted, watch } from 'vue'
   import * as monaco from 'monaco-editor'
   import { useAppEditorStore } from '../stores/app-editor.store'

   const props = defineProps<{
     filePath: string
   }>()

   const emit = defineEmits<{
     change: [content: string]
   }>()

   const editorStore = useAppEditorStore()
   const editorContainer = ref<HTMLElement>()
   let editorInstance: monaco.editor.IStandaloneCodeEditor | null = null

   onMounted(() => {
     if (!editorContainer.value) return

     const file = editorStore.openFiles.find(f => f.path === props.filePath)
     if (!file) return

     // Create Monaco editor
     editorInstance = monaco.editor.create(editorContainer.value, {
       value: file.content,
       language: file.language,
       theme: 'vs-dark',  // Or 'vs-light' based on app theme
       automaticLayout: true,
       minimap: { enabled: true },
       fontSize: 14,
       tabSize: 2,
       wordWrap: 'on'
     })

     // Listen for content changes
     editorInstance.onDidChangeModelContent(() => {
       const content = editorInstance!.getValue()
       editorStore.updateFileContent(props.filePath, content)
       emit('change', content)
     })
   })

   onUnmounted(() => {
     editorInstance?.dispose()
   })

   // Watch for external changes (e.g., AI updates)
   watch(() => {
     const file = editorStore.openFiles.find(f => f.path === props.filePath)
     return file?.content
   }, (newContent) => {
     if (!editorInstance || !newContent) return

     const currentValue = editorInstance.getValue()
     if (currentValue !== newContent) {
       editorInstance.setValue(newContent)
     }
   })

   // Expose save method for parent
   defineExpose({
     save: async () => {
       if (!editorInstance) return
       const content = editorInstance.getValue()
       editorStore.updateFileContent(props.filePath, content)
       await editorStore.saveFile(props.filePath)
     }
   })
   </script>

   <template>
     <div ref="editorContainer" class="w-full h-full"></div>
   </template>

   <style scoped>
   /* Monaco editor styles are global, minimal scoped styles needed */
   </style>
   ```

7. File Tree Component (FileTree.vue):
   ```vue
   <script setup lang="ts">
   import { ref } from 'vue'
   import type { FileNode } from '../types/apps.types'
   import { useAppEditorStore } from '../stores/app-editor.store'
   import { useConfirm } from 'primevue/useconfirm'
   import { useToast } from 'primevue/usetoast'

   const props = defineProps<{
     nodes: FileNode[]
   }>()

   const editorStore = useAppEditorStore()
   const confirm = useConfirm()
   const toast = useToast()
   const contextMenuNode = ref<FileNode | null>(null)

   const getIcon = (node: FileNode) => {
     if (node.type === 'folder') {
       return node.isOpen ? 'pi pi-folder-open' : 'pi pi-folder'
     }

     const ext = node.extension?.toLowerCase()
     if (ext === 'html') return 'pi pi-file-code'
     if (ext === 'css') return 'pi pi-palette'
     if (ext === 'js') return 'pi pi-code'
     if (ext === 'json') return 'pi pi-file'
     return 'pi pi-file'
   }

   const handleNodeClick = async (node: FileNode) => {
     if (node.type === 'folder') {
       node.isOpen = !node.isOpen
       return
     }

     try {
       await editorStore.openFile(node.path)
     } catch (err: any) {
       toast.add({ severity: 'error', summary: 'Error', detail: err.message, life: 3000 })
     }
   }

   const handleContextMenu = (event: MouseEvent, node: FileNode) => {
     event.preventDefault()
     contextMenuNode.value = node
     // Show context menu (PrimeVue Menu component)
   }

   const handleDelete = () => {
     if (!contextMenuNode.value) return

     confirm.require({
       message: `Are you sure you want to delete ${contextMenuNode.value.name}?`,
       header: 'Confirm Delete',
       icon: 'pi pi-exclamation-triangle',
       acceptClass: 'p-button-danger',
       accept: async () => {
         try {
           await editorStore.deleteFile(contextMenuNode.value!.path)
           toast.add({ severity: 'success', summary: 'Deleted', detail: 'File deleted', life: 3000 })
         } catch (err: any) {
           toast.add({ severity: 'error', summary: 'Error', detail: err.message, life: 3000 })
         }
       }
     })
   }

   const isActive = (node: FileNode) => {
     return editorStore.activeFilePath === node.path
   }
   </script>

   <template>
     <div class="file-tree">
       <Tree
         :value="nodes"
         selectionMode="single"
         @node-select="handleNodeClick"
       >
         <template #default="{ node }">
           <div
             class="flex align-items-center gap-2 py-1 px-2 cursor-pointer hover:surface-hover"
             :class="{ 'bg-primary-reverse': isActive(node) }"
             @contextmenu="handleContextMenu($event, node)"
           >
             <i :class="getIcon(node)"></i>
             <span>{{ node.name }}</span>
             <span v-if="editorStore.openFiles.find(f => f.path === node.path && f.isDirty)" class="text-orange-500">
               •
             </span>
           </div>
         </template>
       </Tree>
     </div>
   </template>

   <style scoped>
   .file-tree {
     height: 100%;
     overflow-y: auto;
   }
   </style>
   ```

8. Preview Frame Component (PreviewFrame.vue):
   ```vue
   <script setup lang="ts">
   import { ref, watch, onMounted, onUnmounted } from 'vue'
   import { useAppEditorStore } from '../stores/app-editor.store'

   const editorStore = useAppEditorStore()
   const iframeRef = ref<HTMLIFrameElement>()
   const previewMode = ref<'desktop' | 'tablet' | 'mobile'>('desktop')

   const viewportSizes = {
     desktop: { width: '100%', height: '100%' },
     tablet: { width: '768px', height: '1024px' },
     mobile: { width: '375px', height: '667px' }
   }

   const currentSize = computed(() => viewportSizes[previewMode.value])

   // Refresh iframe when preview HTML changes
   watch(() => editorStore.previewHtml, (newHtml) => {
     if (!iframeRef.value || !newHtml) return

     const doc = iframeRef.value.contentDocument
     if (doc) {
       doc.open()
       doc.write(newHtml)
       doc.close()
     }
   })

   onMounted(() => {
     // Initial load
     if (editorStore.previewHtml && iframeRef.value) {
       const doc = iframeRef.value.contentDocument
       if (doc) {
         doc.open()
         doc.write(editorStore.previewHtml)
         doc.close()
       }
     }
   })
   </script>

   <template>
     <div class="preview-container h-full flex flex-column">
       <!-- Preview Toolbar -->
       <div class="flex align-items-center gap-2 p-2 border-bottom-1 surface-border">
         <Button
           :class="{ 'p-button-outlined': previewMode !== 'desktop' }"
           icon="pi pi-desktop"
           @click="previewMode = 'desktop'"
           size="small"
         />
         <Button
           :class="{ 'p-button-outlined': previewMode !== 'tablet' }"
           icon="pi pi-tablet"
           @click="previewMode = 'tablet'"
           size="small"
         />
         <Button
           :class="{ 'p-button-outlined': previewMode !== 'mobile' }"
           icon="pi pi-mobile"
           @click="previewMode = 'mobile'"
           size="small"
         />
         <Divider layout="vertical" />
         <Button
           icon="pi pi-refresh"
           @click="editorStore.refreshPreview()"
           size="small"
           outlined
         />
       </div>

       <!-- Preview Iframe -->
       <div class="flex-1 overflow-auto bg-gray-100 flex align-items-start justify-content-center p-4">
         <div
           class="preview-viewport bg-white shadow-3"
           :style="{
             width: currentSize.width,
             height: currentSize.height,
             maxWidth: '100%',
             maxHeight: '100%',
             transition: 'width 0.3s, height 0.3s'
           }"
         >
           <iframe
             ref="iframeRef"
             class="w-full h-full border-none"
             sandbox="allow-scripts allow-same-origin allow-forms"
             title="App Preview"
           />
         </div>
       </div>
     </div>
   </template>
   ```

9. Steps Panel Component (StepsPanel.vue):
   ```vue
   <script setup lang="ts">
   import { useAppEditorStore } from '../stores/app-editor.store'
   import { useRouter } from 'vue-router'
   import { useDialog } from 'primevue/usedialog'
   import { useToast } from 'primevue/usetoast'
   import { useConfirm } from 'primevue/useconfirm'

   const editorStore = useAppEditorStore()
   const router = useRouter()
   const dialog = useDialog()
   const toast = useToast()
   const confirm = useConfirm()

   const handleCreateStep = () => {
     // Show create step dialog
     dialog.open(CreateStepDialog, {
       props: {
         header: 'Create Backend Step',
         modal: true
       },
       onClose: async (options) => {
         if (options?.data) {
           try {
             await editorStore.createBackendStep(
               options.data.name,
               options.data.language,
               options.data.description
             )
             toast.add({ severity: 'success', summary: 'Created', detail: 'Step created and linked', life: 3000 })
           } catch (err: any) {
             toast.add({ severity: 'error', summary: 'Error', detail: err.message, life: 3000 })
           }
         }
       }
     })
   }

   const handleEditStep = (stepId: string) => {
     // Navigate to step designer
     router.push(`/steps/${stepId}/edit`)
   }

   const handleViewCode = async (stepId: string) => {
     // Show step code in dialog
     dialog.open(StepCodeDialog, {
       props: {
         header: 'Step Code',
         modal: true,
         style: { width: '800px' }
       },
       data: { stepId }
     })
   }

   const handleUnlink = (stepId: string, stepName: string) => {
     confirm.require({
       message: `Unlink step "${stepName}" from this app?`,
       header: 'Confirm Unlink',
       icon: 'pi pi-exclamation-triangle',
       accept: async () => {
         try {
           await editorStore.unlinkStep(stepId)
           toast.add({ severity: 'success', summary: 'Unlinked', detail: 'Step unlinked from app', life: 3000 })
         } catch (err: any) {
           toast.add({ severity: 'error', summary: 'Error', detail: err.message, life: 3000 })
         }
       }
     })
   }

   const getLanguageIcon = (language: string) => {
     if (language === 'Python') return 'pi pi-code'
     if (language === 'PowerShell') return 'pi pi-terminal'
     if (language === 'Batch') return 'pi pi-desktop'
     return 'pi pi-file'
   }
   </script>

   <template>
     <div class="steps-panel h-full flex flex-column">
       <!-- Header -->
       <div class="flex align-items-center justify-content-between p-3 border-bottom-1 surface-border">
         <h3 class="m-0">Backend Steps</h3>
         <Button
           icon="pi pi-plus"
           label="New Step"
           size="small"
           @click="handleCreateStep"
         />
       </div>

       <!-- Steps List -->
       <div class="flex-1 overflow-auto p-3">
         <div v-if="editorStore.linkedSteps.length === 0" class="text-center text-500 py-4">
           No backend steps linked
         </div>

         <div v-else class="flex flex-column gap-2">
           <Card
             v-for="step in editorStore.linkedSteps"
             :key="step.id"
             class="step-card"
           >
             <template #content>
               <div class="flex align-items-start gap-2">
                 <i :class="getLanguageIcon(step.language)" class="text-2xl text-500"></i>
                 <div class="flex-1">
                   <div class="font-semibold mb-1">{{ step.displayName || step.name }}</div>
                   <div class="text-sm text-500 mb-2">{{ step.description }}</div>
                   <div class="flex align-items-center gap-3 text-xs text-500">
                     <span>{{ step.inputCount }} inputs</span>
                     <span>{{ step.outputCount }} outputs</span>
                     <Tag :value="step.language" size="small" severity="info" />
                   </div>
                 </div>
               </div>

               <div class="flex gap-2 mt-3">
                 <Button
                   label="Edit"
                   icon="pi pi-pencil"
                   size="small"
                   outlined
                   @click="handleEditStep(step.id)"
                 />
                 <Button
                   label="View Code"
                   icon="pi pi-eye"
                   size="small"
                   text
                   @click="handleViewCode(step.id)"
                 />
                 <Button
                   icon="pi pi-times"
                   size="small"
                   text
                   severity="danger"
                   @click="handleUnlink(step.id, step.displayName || step.name)"
                 />
               </div>
             </template>
           </Card>
         </div>
       </div>
     </div>
   </template>

   <style scoped>
   .step-card {
     transition: box-shadow 0.2s;
   }

   .step-card:hover {
     box-shadow: 0 2px 8px rgba(0, 0, 0, 0.1);
   }
   </style>
   ```

10. AI Assistant Component (AiAssistant.vue):

    **IMPORTANT**: This is a placeholder for MCP integration. Full AI implementation requires:
    - WebSocket/SignalR connection to MCP server
    - Message handling for AI responses
    - Tool execution feedback
    - Code update synchronization with editor

    ```vue
    <script setup lang="ts">
    import { ref } from 'vue'
    import { useAppEditorStore } from '../stores/app-editor.store'

    const editorStore = useAppEditorStore()
    const messages = ref<Array<{ role: 'user' | 'assistant', content: string }>>([])
    const userInput = ref('')
    const loading = ref(false)

    const sendMessage = async () => {
      if (!userInput.value.trim()) return

      const message = userInput.value
      userInput.value = ''

      messages.value.push({ role: 'user', content: message })
      loading.value = true

      try {
        // TODO: Implement MCP client connection
        // For now, placeholder response
        messages.value.push({
          role: 'assistant',
          content: 'AI assistant is not yet implemented. This will connect to MCP server for app generation.'
        })
      } catch (err: any) {
        messages.value.push({
          role: 'assistant',
          content: `Error: ${err.message}`
        })
      } finally {
        loading.value = false
      }
    }
    </script>

    <template>
      <div class="ai-assistant h-full flex flex-column">
        <!-- Messages -->
        <div class="flex-1 overflow-auto p-3 bg-gray-50">
          <div v-for="(msg, index) in messages" :key="index" class="mb-3">
            <div
              class="p-3 border-round"
              :class="msg.role === 'user' ? 'bg-primary-reverse ml-8' : 'bg-white mr-8'"
            >
              <div class="text-xs text-500 mb-1">{{ msg.role === 'user' ? 'You' : 'AI' }}</div>
              <div>{{ msg.content }}</div>
            </div>
          </div>
          <div v-if="loading" class="p-3 bg-white border-round mr-8">
            <i class="pi pi-spin pi-spinner"></i> AI is thinking...
          </div>
        </div>

        <!-- Input -->
        <div class="p-3 border-top-1 surface-border">
          <div class="flex gap-2">
            <InputText
              v-model="userInput"
              placeholder="Ask AI to create or modify your app..."
              class="flex-1"
              @keyup.enter="sendMessage"
            />
            <Button
              icon="pi pi-send"
              @click="sendMessage"
              :disabled="loading || !userInput.trim()"
            />
          </div>
        </div>
      </div>
    </template>
    ```

11. Main App Editor View (AppEditorView.vue):
    ```vue
    <script setup lang="ts">
    import { onMounted, computed, ref } from 'vue'
    import { useRoute, useRouter } from 'vue-router'
    import { useAppEditorStore } from '../stores/app-editor.store'
    import { useDialog } from 'primevue/usedialog'
    import { useToast } from 'primevue/usetoast'
    import FileTree from '../components/FileTree.vue'
    import CodeEditor from '../components/CodeEditor.vue'
    import PreviewFrame from '../components/PreviewFrame.vue'
    import StepsPanel from '../components/StepsPanel.vue'
    import AiAssistant from '../components/AiAssistant.vue'

    const route = useRoute()
    const router = useRouter()
    const editorStore = useAppEditorStore()
    const dialog = useDialog()
    const toast = useToast()

    const appId = computed(() => route.params.id as string)
    const activeTab = ref<'editor' | 'preview' | 'steps'>('editor')
    const showAiPanel = ref(false)

    onMounted(async () => {
      try {
        await editorStore.loadApp(appId.value)
      } catch (err: any) {
        toast.add({ severity: 'error', summary: 'Error', detail: err.message, life: 5000 })
        router.push('/apps')
      }
    })

    const handleSave = async () => {
      try {
        await editorStore.saveAllFiles()
        toast.add({ severity: 'success', summary: 'Saved', detail: 'All files saved', life: 3000 })
      } catch (err: any) {
        toast.add({ severity: 'error', summary: 'Error', detail: err.message, life: 3000 })
      }
    }

    const handlePublish = () => {
      dialog.open(PublishDialog, {
        props: {
          header: 'Publish App',
          modal: true
        },
        data: {
          currentSlug: editorStore.currentApp?.publicUrl,
          requiresAuth: editorStore.currentApp?.requiresAuthentication || false
        },
        onClose: async (options) => {
          if (options?.data) {
            try {
              await editorStore.publishApp(
                options.data.slug,
                options.data.requiresAuth,
                options.data.runnerId
              )
              toast.add({ severity: 'success', summary: 'Published', detail: `App published at /web/${options.data.slug}`, life: 5000 })
            } catch (err: any) {
              toast.add({ severity: 'error', summary: 'Error', detail: err.message, life: 3000 })
            }
          }
        }
      })
    }

    const handleBack = () => {
      if (editorStore.hasUnsavedChanges) {
        // Show confirmation dialog
        // ...
      } else {
        router.push('/apps')
      }
    }
    </script>

    <template>
      <div class="app-editor h-full flex flex-column">
        <!-- Top Toolbar -->
        <div class="flex align-items-center justify-content-between p-3 border-bottom-1 surface-border">
          <div class="flex align-items-center gap-3">
            <Button
              icon="pi pi-arrow-left"
              text
              @click="handleBack"
            />
            <div>
              <h2 class="m-0">{{ editorStore.currentApp?.name || 'Loading...' }}</h2>
              <div class="text-sm text-500">
                {{ editorStore.currentApp?.description }}
              </div>
            </div>
          </div>

          <div class="flex gap-2">
            <Button
              label="Save"
              icon="pi pi-save"
              :disabled="!editorStore.hasUnsavedChanges"
              :loading="editorStore.saving"
              @click="handleSave"
            />
            <Button
              v-if="!editorStore.currentApp?.isPublished"
              label="Publish"
              icon="pi pi-cloud-upload"
              @click="handlePublish"
            />
            <Button
              icon="pi pi-sparkles"
              :label="showAiPanel ? 'Hide AI' : 'Show AI'"
              outlined
              @click="showAiPanel = !showAiPanel"
            />
          </div>
        </div>

        <!-- Main Content Area -->
        <div class="flex-1 flex overflow-hidden">
          <!-- Left Panel: File Tree -->
          <div class="w-15rem border-right-1 surface-border overflow-auto">
            <FileTree :nodes="editorStore.fileTree" />
          </div>

          <!-- Center Panel: Editor + Tabs -->
          <div class="flex-1 flex flex-column">
            <!-- Tab Headers -->
            <div class="flex gap-2 p-2 border-bottom-1 surface-border overflow-x-auto">
              <Button
                v-for="file in editorStore.openFiles"
                :key="file.path"
                :label="file.path"
                :icon="file.isDirty ? 'pi pi-circle-fill' : ''"
                size="small"
                :outlined="editorStore.activeFilePath !== file.path"
                @click="editorStore.activeFilePath = file.path"
              >
                <template #icon>
                  <span v-if="file.isDirty" class="text-orange-500 mr-2">•</span>
                </template>
              </Button>
            </div>

            <!-- Editor Content -->
            <div class="flex-1 overflow-hidden">
              <CodeEditor
                v-if="editorStore.activeFilePath"
                :key="editorStore.activeFilePath"
                :file-path="editorStore.activeFilePath"
              />
              <div v-else class="flex align-items-center justify-content-center h-full text-500">
                Select a file to edit
              </div>
            </div>
          </div>

          <!-- Right Panel: Preview OR Steps -->
          <div class="w-30rem border-left-1 surface-border flex flex-column">
            <!-- Panel Tabs -->
            <div class="flex border-bottom-1 surface-border">
              <Button
                label="Preview"
                :outlined="activeTab !== 'preview'"
                @click="activeTab = 'preview'"
                class="flex-1"
              />
              <Button
                label="Backend"
                :outlined="activeTab !== 'steps'"
                @click="activeTab = 'steps'"
                class="flex-1"
              />
            </div>

            <!-- Panel Content -->
            <div class="flex-1 overflow-hidden">
              <PreviewFrame v-if="activeTab === 'preview'" />
              <StepsPanel v-else-if="activeTab === 'steps'" />
            </div>
          </div>
        </div>

        <!-- Bottom Panel: AI Assistant (collapsible) -->
        <div
          v-if="showAiPanel"
          class="h-20rem border-top-1 surface-border"
        >
          <AiAssistant />
        </div>
      </div>
    </template>

    <style scoped>
    .app-editor {
      background: var(--surface-ground);
    }
    </style>
    ```

Deliverables:
- [ ] App loads with file tree, editor, and preview
- [ ] Monaco Editor displays and edits files
- [ ] File tree shows app structure
- [ ] Can open/close/switch between files
- [ ] Tabs show open files with dirty indicator (•)
- [ ] Save button saves all files
- [ ] Preview iframe displays app with automatonApi.js injected
- [ ] Preview toolbar with Desktop/Tablet/Mobile toggle works
- [ ] Backend Steps panel lists linked steps
- [ ] Can create new backend step (dialog)
- [ ] Can view step code (dialog)
- [ ] Can unlink step (confirmation)
- [ ] Can navigate to Step Designer (Edit button)
- [ ] Publish dialog allows setting slug and auth requirements
- [ ] Publish button creates public URL
- [ ] AI Assistant panel toggles (placeholder implementation)
- [ ] Unsaved changes indicator works
- [ ] Back button with unsaved changes confirmation
- [ ] Loading states for app load
- [ ] Error handling for all operations
- [ ] Responsive layout (minimum 1280px width recommended)
- [ ] Dark mode support

Testing Checklist:
- [ ] Test app load (should show file tree + index.html)
- [ ] Test opening multiple files (tabs appear)
- [ ] Test switching between files (editor content changes)
- [ ] Test editing file (dirty indicator appears)
- [ ] Test save (dirty indicator clears)
- [ ] Test close file with unsaved changes (confirmation)
- [ ] Test file tree navigation
- [ ] Test Monaco Editor features:
  - Syntax highlighting
  - Auto-completion
  - Error detection
  - Find/replace (Ctrl+F)
  - Multi-cursor (Alt+Click)
- [ ] Test preview iframe:
  - Initial render
  - Auto-refresh on save
  - Desktop/Tablet/Mobile modes
  - Responsive toggle animation
- [ ] Test backend steps:
  - List displays correctly
  - Create step dialog works
  - View code dialog shows step code
  - Unlink confirmation works
  - Edit navigates to Step Designer
- [ ] Test publish:
  - Dialog shows current slug (if published)
  - Slug validation (lowercase, alphanumeric, hyphens)
  - Duplicate slug error
  - Success message shows public URL
  - App becomes published (badge changes)
- [ ] Test AI panel toggle
- [ ] Test back navigation with unsaved changes
- [ ] Test error scenarios:
  - App not found (404)
  - File save failure
  - Preview load failure
  - Step creation failure
- [ ] Verify no console errors
- [ ] Verify no TypeScript errors

API Integration Notes:
- App Editor requires multiple API endpoints:
  - GET /api/apps/{id} - Load app metadata
  - GET /api/apps/{id}/definition - Load app files
  - GET /api/apps/{id}/files - Get file tree
  - GET /api/apps/{id}/files/{path} - Get file content
  - PUT /api/apps/{id}/files/{path} - Save file
  - POST /api/apps/{id}/files - Create file
  - DELETE /api/apps/{id}/files/{path} - Delete file
  - GET /api/apps/{id}/preview - Get preview HTML with automatonApi.js
  - POST /api/apps/{id}/publish - Publish app
  - GET /api/apps/{id}/steps - Get linked steps
  - POST /api/apps/{id}/steps - Create and link step
  - DELETE /api/apps/{id}/steps/{stepId} - Unlink step
  - GET /api/custom-steps/{stepId}/code - Get step code

- Review AppsController.cs and AppFileStorageService.cs for exact endpoint contracts
- Some endpoints may need to be created (file tree, individual file CRUD)
- Preview endpoint must inject automatonApi.js script

Monaco Editor Setup:
- Import Monaco Editor correctly (may need webpack config or Vite plugin)
- Configure Monaco for HTML/CSS/JS languages
- Set up dark/light theme based on app theme
- Enable IntelliSense for JavaScript
- Consider adding Emmet support for HTML

Performance Considerations:
- Debounce file content changes (don't save on every keystroke)
- Lazy load preview iframe (only when tab is active)
- Consider virtual scrolling for large file trees
- Cache file content when switching tabs
- Dispose Monaco editor instances when closing files

Security:
- Sanitize file paths (prevent directory traversal)
- Validate file extensions (whitelist: .html, .css, .js, .json, images)
- Sandbox iframe with appropriate permissions
- Encode file paths in URLs

CRITICAL: This is a full Visual IDE. Focus on developer experience, performance, and reliability.

MCP Integration (Future Phase):
- AI Assistant needs WebSocket/SignalR connection to MCP server
- Implement AppMcpClient equivalent in Vue
- Handle tool execution feedback (code updates from AI)
- Synchronize editor content when AI modifies code
- Show AI progress/status during generation
- Handle backend step creation events from AI

NOTES:
- Monaco Editor is the same editor used in VS Code - very powerful
- Three-panel layout: Files | Editor | Preview
- Resizable panels using PrimeVue Splitter component (optional enhancement)
- File tabs allow editing multiple files simultaneously
- Backend steps are managed separately from file editing
- Preview uses iframe with message passing (postMessage) for security
- AI Assistant is placeholder - full implementation requires MCP client
```

---

## Implementation Checklist

### Prerequisites
- [ ] Phase 3.3a (Apps List) completed
- [ ] Monaco Editor package installed
- [ ] Review Blazor AppPage implementation
- [ ] Review AppsController and AppFileStorageService APIs

### Files to Create (11 new files)
- [ ] features/apps/services/app-files.service.ts
- [ ] features/apps/stores/app-editor.store.ts
- [ ] features/apps/components/FileTree.vue
- [ ] features/apps/components/CodeEditor.vue
- [ ] features/apps/components/PreviewFrame.vue
- [ ] features/apps/components/StepsPanel.vue
- [ ] features/apps/components/AiAssistant.vue (placeholder)
- [ ] features/apps/components/PublishDialog.vue
- [ ] features/apps/components/CreateStepDialog.vue
- [ ] features/apps/components/StepCodeDialog.vue
- [ ] features/apps/components/CreateFileDialog.vue

### Files to Extend
- [ ] features/apps/types/apps.types.ts (add editor types)
- [ ] features/apps/services/apps.service.ts (add publish methods)
- [ ] features/apps/views/AppEditorView.vue (replace stub)

### Backend Verification
- [ ] Verify all file CRUD endpoints exist
- [ ] Verify preview HTML endpoint with script injection
- [ ] Verify publish/unpublish endpoints
- [ ] Verify step linking endpoints
- [ ] Test automatonApi.js injection in preview

### Testing
- [ ] All deliverables checked
- [ ] All test cases passed
- [ ] Monaco Editor works correctly
- [ ] Preview iframe works
- [ ] File management works
- [ ] Steps panel works
- [ ] Publish workflow works

---

## Architecture Notes

**Three-Panel Layout**:
- Left: File Tree (15rem width)
- Center: Code Editor (flex-1, expandable)
- Right: Preview/Steps (30rem width)

**State Management**:
- App Editor Store handles:
  - Current app metadata
  - Open files with dirty tracking
  - Active file path
  - Linked backend steps
  - Preview HTML cache

**Monaco Editor Integration**:
- One instance per open file
- Lazy instantiation (create on tab switch)
- Proper disposal to prevent memory leaks
- Theme sync with app theme

**Preview System**:
- Iframe sandboxed with `allow-scripts allow-same-origin allow-forms`
- HTML injected via `doc.write()` for full control
- automatonApi.js provides `window.automatonApi` object
- Message passing for API calls (future enhancement)

**Backend Steps Workflow**:
1. Create step → Links automatically to current app
2. Edit step → Navigate to Step Designer
3. View code → Read-only dialog with usage example
4. Unlink → Remove from app (step still exists)

**File Management**:
- File tree built from API response
- Files loaded on demand (not all at once)
- Auto-save on Ctrl+S or Save button
- Dirty tracking prevents data loss

**AI Integration (Future)**:
- AI Assistant panel at bottom (collapsible)
- WebSocket connection to MCP server
- Tool execution updates editor in real-time
- Step creation events update Steps panel

This is a **production-ready Visual IDE** for building full-stack web apps with AI assistance!
