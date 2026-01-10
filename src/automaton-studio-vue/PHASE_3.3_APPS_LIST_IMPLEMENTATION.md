# Phase 3.3 - Apps List Page Implementation

**AI Agent Prompt for Apps List Page Implementation**

---

## Prompt for AI Agent

```
Implement Phase 3.3 - Apps List Page from REDESIGN_PLAN.md.

PREREQUISITE: Before starting, READ THE ENTIRE src/automaton-studio-vue/REDESIGN_PLAN.md file.
Pay special attention to:
- Section 6: Project Structure (Feature-Based) - work in features/apps/ structure
- Section 7.3: Apps List Page design spec - follow exact layout
- Common Pitfalls for AI Agents - avoid these mistakes!
- Phase 1.2 completed: Apps List stub already exists, you're adding functionality

IMPORTANT:
- Apps List stub already exists from Phase 1.2 - REPLACE placeholder/mock data with real API calls
- Follow Project Structure (Section 6) for features/apps/ folder
- Review Apps List design spec (Section 7.3) for exact layout (already implemented in stub)
- Review Blazor Apps implementation for feature parity:
  - src/Automaton.Studio/Pages/Apps/AppsPage.razor
  - Backend: src/Automaton.Studio.Server/Controllers/AppsController.cs
  - Backend: src/Automaton.Studio.Server/Services/AppsService.cs
- This is PART 1 of Apps feature (List only - Editor is separate in Phase 3.3b)

Key Implementation Tasks:

1. Apps Feature Structure (ADD to existing):
   - features/apps/types/apps.types.ts (NEW)
   - features/apps/services/apps.service.ts (NEW)
   - features/apps/stores/apps.store.ts (NEW)
   - features/apps/components/AppCard.vue (NEW)
   - features/apps/views/AppListView.vue (EXISTS - UPDATE)

2. TypeScript Interfaces (apps.types.ts):
   ```typescript
   export interface App {
     id: string
     name: string
     description: string
     publicUrl: string  // Slug for published apps (e.g., "customer-portal")
     isPublished: boolean
     requiresAuthentication: boolean
     fileCount: number  // Number of files in the app
     backendStepCount: number  // Number of linked backend steps
     updatedAt: string  // ISO date string
     createdAt: string
     ownerId: string
     projectId?: string  // Optional - if app belongs to a project
   }

   export interface AppListFilters {
     search: string
     status: 'all' | 'published' | 'draft'
     projectId?: string  // Filter by project
   }

   export interface CreateAppRequest {
     name: string
     description: string
     projectId?: string
   }
   ```

3. Apps Service (apps.service.ts):
   ```typescript
   import axios from '@/shared/services/api/axios-instance'
   import type { App, CreateAppRequest } from '../types/apps.types'

   export const appsService = {
     // Get all apps (with optional project filter)
     async list(projectId?: string): Promise<App[]> {
       const params = projectId ? { projectId } : {}
       const response = await axios.get('/api/apps', { params })
       return response.data
     },

     // Get single app by ID
     async get(id: string): Promise<App> {
       const response = await axios.get(`/api/apps/${id}`)
       return response.data
     },

     // Create new app
     async create(data: CreateAppRequest): Promise<App> {
       const response = await axios.post('/api/apps', data)
       return response.data
     },

     // Update app
     async update(id: string, data: Partial<App>): Promise<App> {
       const response = await axios.put(`/api/apps/${id}`, data)
       return response.data
     },

     // Delete app
     async delete(id: string): Promise<void> {
       await axios.delete(`/api/apps/${id}`)
     },

     // Publish app
     async publish(id: string): Promise<App> {
       const response = await axios.post(`/api/apps/${id}/publish`)
       return response.data
     },

     // Unpublish app
     async unpublish(id: string): Promise<App> {
       const response = await axios.post(`/api/apps/${id}/unpublish`)
       return response.data
     },

     // Duplicate app
     async duplicate(id: string): Promise<App> {
       const response = await axios.post(`/api/apps/${id}/duplicate`)
       return response.data
     }
   }
   ```

4. Apps Store (apps.store.ts - Pinia):
   ```typescript
   import { defineStore } from 'pinia'
   import { appsService } from '../services/apps.service'
   import type { App, AppListFilters } from '../types/apps.types'

   export const useAppsStore = defineStore('apps', {
     state: () => ({
       apps: [] as App[],
       selectedApp: null as App | null,
       loading: false,
       error: null as string | null,
       filters: {
         search: '',
         status: 'all',
         projectId: undefined
       } as AppListFilters
     }),

     getters: {
       filteredApps: (state) => {
         let filtered = state.apps

         // Filter by status
         if (state.filters.status === 'published') {
           filtered = filtered.filter(app => app.isPublished)
         } else if (state.filters.status === 'draft') {
           filtered = filtered.filter(app => !app.isPublished)
         }

         // Filter by search
         if (state.filters.search) {
           const search = state.filters.search.toLowerCase()
           filtered = filtered.filter(app =>
             app.name.toLowerCase().includes(search) ||
             app.description.toLowerCase().includes(search)
           )
         }

         return filtered
       },

       publishedApps: (state) => state.apps.filter(app => app.isPublished),
       draftApps: (state) => state.apps.filter(app => !app.isPublished),

       appCount: (state) => state.apps.length,
       publishedCount: (state) => state.apps.filter(app => app.isPublished).length,
       draftCount: (state) => state.apps.filter(app => !app.isPublished).length
     },

     actions: {
       async fetchApps(projectId?: string) {
         this.loading = true
         this.error = null
         try {
           this.apps = await appsService.list(projectId)
         } catch (err: any) {
           this.error = err.response?.data?.message || 'Failed to load apps'
           throw err
         } finally {
           this.loading = false
         }
       },

       async createApp(name: string, description: string, projectId?: string) {
         try {
           const newApp = await appsService.create({ name, description, projectId })
           this.apps.push(newApp)
           return newApp
         } catch (err: any) {
           throw new Error(err.response?.data?.message || 'Failed to create app')
         }
       },

       async deleteApp(id: string) {
         try {
           await appsService.delete(id)
           this.apps = this.apps.filter(app => app.id !== id)
         } catch (err: any) {
           throw new Error(err.response?.data?.message || 'Failed to delete app')
         }
       },

       async publishApp(id: string) {
         try {
           const updated = await appsService.publish(id)
           const index = this.apps.findIndex(app => app.id === id)
           if (index !== -1) {
             this.apps[index] = updated
           }
         } catch (err: any) {
           throw new Error(err.response?.data?.message || 'Failed to publish app')
         }
       },

       async unpublishApp(id: string) {
         try {
           const updated = await appsService.unpublish(id)
           const index = this.apps.findIndex(app => app.id === id)
           if (index !== -1) {
             this.apps[index] = updated
           }
         } catch (err: any) {
           throw new Error(err.response?.data?.message || 'Failed to unpublish app')
         }
       },

       async duplicateApp(id: string) {
         try {
           const duplicated = await appsService.duplicate(id)
           this.apps.push(duplicated)
           return duplicated
         } catch (err: any) {
           throw new Error(err.response?.data?.message || 'Failed to duplicate app')
         }
       },

       setSearchFilter(search: string) {
         this.filters.search = search
       },

       setStatusFilter(status: 'all' | 'published' | 'draft') {
         this.filters.status = status
       }
     }
   })
   ```

5. App Card Component (AppCard.vue):
   ```vue
   <script setup lang="ts">
   import { computed } from 'vue'
   import type { App } from '../types/apps.types'
   import { useRouter } from 'vue-router'
   import { useConfirm } from 'primevue/useconfirm'
   import { useToast } from 'primevue/usetoast'
   import { useAppsStore } from '../stores/apps.store'

   interface Props {
     app: App
   }

   const props = defineProps<Props>()
   const router = useRouter()
   const confirm = useConfirm()
   const toast = useToast()
   const appsStore = useAppsStore()

   const statusBadge = computed(() => ({
     label: props.app.isPublished ? 'Published' : 'Draft',
     severity: props.app.isPublished ? 'success' : 'secondary',
     icon: props.app.isPublished ? '✅' : '📝'
   }))

   const updatedAgo = computed(() => {
     const now = new Date()
     const updated = new Date(props.app.updatedAt)
     const diffMs = now.getTime() - updated.getTime()
     const diffHours = Math.floor(diffMs / (1000 * 60 * 60))

     if (diffHours < 1) return 'Updated: just now'
     if (diffHours < 24) return `Updated: ${diffHours}h ago`

     const diffDays = Math.floor(diffHours / 24)
     if (diffDays < 7) return `Updated: ${diffDays}d ago`

     const diffWeeks = Math.floor(diffDays / 7)
     return `Updated: ${diffWeeks}w ago`
   })

   const metadataText = computed(() => {
     const parts = []
     if (props.app.fileCount) parts.push(`${props.app.fileCount} ${props.app.fileCount === 1 ? 'file' : 'files'}`)
     if (props.app.backendStepCount) parts.push(`${props.app.backendStepCount} ${props.app.backendStepCount === 1 ? 'step' : 'steps'}`)
     return parts.join(', ') || 'No files'
   })

   const handleEdit = () => {
     router.push(`/apps/${props.app.id}/edit`)
   }

   const handleView = () => {
     if (props.app.isPublished) {
       window.open(`/web/${props.app.publicUrl}`, '_blank')
     }
   }

   const handlePublish = async () => {
     try {
       await appsStore.publishApp(props.app.id)
       toast.add({ severity: 'success', summary: 'Published', detail: 'App has been published', life: 3000 })
     } catch (err: any) {
       toast.add({ severity: 'error', summary: 'Error', detail: err.message, life: 3000 })
     }
   }

   const handleUnpublish = async () => {
     confirm.require({
       message: 'Are you sure you want to unpublish this app?',
       header: 'Confirm Unpublish',
       icon: 'pi pi-exclamation-triangle',
       accept: async () => {
         try {
           await appsStore.unpublishApp(props.app.id)
           toast.add({ severity: 'success', summary: 'Unpublished', detail: 'App has been unpublished', life: 3000 })
         } catch (err: any) {
           toast.add({ severity: 'error', summary: 'Error', detail: err.message, life: 3000 })
         }
       }
     })
   }

   const handleDuplicate = async () => {
     try {
       const duplicated = await appsStore.duplicateApp(props.app.id)
       toast.add({ severity: 'success', summary: 'Duplicated', detail: 'App has been duplicated', life: 3000 })
       router.push(`/apps/${duplicated.id}/edit`)
     } catch (err: any) {
       toast.add({ severity: 'error', summary: 'Error', detail: err.message, life: 3000 })
     }
   }

   const handleDelete = () => {
     confirm.require({
       message: 'Are you sure you want to delete this app? This action cannot be undone.',
       header: 'Confirm Delete',
       icon: 'pi pi-exclamation-triangle',
       acceptClass: 'p-button-danger',
       accept: async () => {
         try {
           await appsStore.deleteApp(props.app.id)
           toast.add({ severity: 'success', summary: 'Deleted', detail: 'App has been deleted', life: 3000 })
         } catch (err: any) {
           toast.add({ severity: 'error', summary: 'Error', detail: err.message, life: 3000 })
         }
       }
     })
   }

   const menuItems = computed(() => {
     const items = [
       {
         label: 'Duplicate',
         icon: 'pi pi-copy',
         command: handleDuplicate
       }
     ]

     if (props.app.isPublished) {
       items.push({
         label: 'Unpublish',
         icon: 'pi pi-eye-slash',
         command: handleUnpublish
       })
     }

     items.push({
       label: 'Delete',
       icon: 'pi pi-trash',
       command: handleDelete
     })

     return items
   })
   </script>

   <template>
     <Card class="app-card h-full">
       <template #header>
         <div class="p-4 pb-0">
           <div class="flex align-items-center gap-2 mb-2">
             <span class="text-2xl">🌐</span>
             <h3 class="text-xl font-semibold m-0">{{ app.name }}</h3>
           </div>
         </div>
       </template>

       <template #content>
         <p class="text-sm text-color-secondary mb-3 line-clamp-2">
           {{ app.description || 'No description' }}
         </p>

         <div class="flex align-items-center gap-2 mb-3">
           <Tag :severity="statusBadge.severity">
             {{ statusBadge.icon }} {{ statusBadge.label }}
           </Tag>
         </div>

         <div class="text-xs text-color-secondary mb-2">
           {{ metadataText }}
         </div>
         <div class="text-xs text-color-secondary">
           {{ updatedAgo }}
         </div>
       </template>

       <template #footer>
         <div class="flex gap-2">
           <Button
             label="Edit"
             size="small"
             @click="handleEdit"
           />
           <Button
             v-if="app.isPublished"
             label="View"
             size="small"
             outlined
             @click="handleView"
           />
           <Button
             v-else
             label="Publish"
             size="small"
             outlined
             @click="handlePublish"
           />
           <Button
             icon="pi pi-ellipsis-v"
             size="small"
             text
             @click="$refs.menu.toggle($event)"
           />
         </div>
         <Menu ref="menu" :model="menuItems" popup />
       </template>
     </Card>
   </template>

   <style scoped>
   .app-card {
     transition: box-shadow 0.2s;
   }

   .app-card:hover {
     box-shadow: 0 4px 20px rgba(0, 0, 0, 0.1);
   }

   .line-clamp-2 {
     display: -webkit-box;
     -webkit-line-clamp: 2;
     -webkit-box-orient: vertical;
     overflow: hidden;
   }
   </style>
   ```

6. Update App List View (AppListView.vue):
   ```vue
   <script setup lang="ts">
   import { ref, onMounted, computed } from 'vue'
   import { useAppsStore } from '../stores/apps.store'
   import { useRouter } from 'vue-router'
   import AppCard from '../components/AppCard.vue'
   import { useDialog } from 'primevue/usedialog'
   import { useToast } from 'primevue/usetoast'

   const appsStore = useAppsStore()
   const router = useRouter()
   const dialog = useDialog()
   const toast = useToast()

   const searchQuery = ref('')
   const selectedStatus = ref<'all' | 'published' | 'draft'>('all')

   const statusOptions = [
     { label: 'All', value: 'all' },
     { label: 'Published', value: 'published' },
     { label: 'Draft', value: 'draft' }
   ]

   onMounted(async () => {
     await appsStore.fetchApps()
   })

   const handleSearch = (value: string) => {
     appsStore.setSearchFilter(value)
   }

   const handleStatusChange = (value: 'all' | 'published' | 'draft') => {
     appsStore.setStatusFilter(value)
   }

   const handleCreateApp = () => {
     // Show create dialog
     const newAppName = ref('')
     const newAppDescription = ref('')

     dialog.open(CreateAppDialog, {
       props: {
         header: 'Create New App',
         style: { width: '450px' },
         modal: true
       },
       data: {
         name: newAppName,
         description: newAppDescription
       },
       onClose: async (options) => {
         if (options?.data) {
           try {
             const app = await appsStore.createApp(options.data.name, options.data.description)
             toast.add({ severity: 'success', summary: 'Created', detail: 'App created successfully', life: 3000 })
             router.push(`/apps/${app.id}/edit`)
           } catch (err: any) {
             toast.add({ severity: 'error', summary: 'Error', detail: err.message, life: 3000 })
           }
         }
       }
     })
   }

   const handleCreateWithAI = () => {
     // Navigate to AI-powered app creation wizard
     router.push('/apps/new/ai')
   }

   const handleViewExamples = () => {
     // Navigate to examples page or open modal with templates
     router.push('/apps/examples')
   }
   </script>

   <template>
     <div class="p-4">
       <!-- Page Header -->
       <div class="flex align-items-center justify-content-between mb-6">
         <h1 class="text-3xl font-bold">Apps</h1>
         <Button
           icon="pi pi-plus"
           label="New App"
           @click="handleCreateApp"
         />
       </div>

       <!-- Search and Filters -->
       <div class="flex gap-3 mb-6">
         <span class="p-input-icon-left flex-1">
           <i class="pi pi-search" />
           <InputText
             v-model="searchQuery"
             placeholder="Search apps..."
             class="w-full"
             @input="handleSearch(searchQuery)"
           />
         </span>
         <Dropdown
           v-model="selectedStatus"
           :options="statusOptions"
           optionLabel="label"
           optionValue="value"
           placeholder="Status"
           @change="handleStatusChange(selectedStatus)"
         />
       </div>

       <!-- Loading State -->
       <div v-if="appsStore.loading" class="grid">
         <div class="col-12 md:col-4" v-for="i in 6" :key="i">
           <Skeleton height="300px" />
         </div>
       </div>

       <!-- Error State -->
       <Message v-else-if="appsStore.error" severity="error" :closable="false">
         <div class="flex align-items-center justify-content-between">
           <span>{{ appsStore.error }}</span>
           <Button
             label="Retry"
             severity="danger"
             @click="appsStore.fetchApps()"
             size="small"
           />
         </div>
       </Message>

       <!-- Content -->
       <div v-else>
         <!-- Apps Grid -->
         <div v-if="appsStore.filteredApps.length > 0">
           <div class="flex align-items-center justify-content-between mb-4">
             <h2 class="text-xl font-semibold">
               My Apps ({{ appsStore.filteredApps.length }})
             </h2>
           </div>

           <div class="grid">
             <div
               v-for="app in appsStore.filteredApps"
               :key="app.id"
               class="col-12 md:col-6 lg:col-4"
             >
               <AppCard :app="app" />
             </div>
           </div>
         </div>

         <!-- Empty State -->
         <Card v-else class="text-center py-8">
           <template #content>
             <i class="pi pi-inbox text-6xl text-400 mb-4"></i>
             <h3 class="text-xl font-semibold mb-2">No apps found</h3>
             <p class="text-color-secondary mb-4">
               {{ searchQuery ? 'Try adjusting your search or filters' : 'Get started by creating your first app' }}
             </p>
             <Button
               v-if="!searchQuery"
               label="Create App"
               icon="pi pi-plus"
               @click="handleCreateApp"
             />
           </template>
         </Card>

         <!-- Quick Start Section (only show when no search/filters) -->
         <Card v-if="appsStore.apps.length === 0 && !searchQuery" class="mt-4">
           <template #title>
             <div class="flex align-items-center gap-2">
               <i class="pi pi-lightbulb"></i>
               <span>Quick Start</span>
             </div>
           </template>
           <template #content>
             <p class="mb-3">New to apps? Create your first web app with AI:</p>
             <ul class="list-none p-0 m-0 mb-4">
               <li class="mb-2">• Simple data entry form</li>
               <li class="mb-2">• Dashboard with charts</li>
               <li class="mb-2">• Customer feedback portal</li>
             </ul>
             <div class="flex gap-2">
               <Button
                 label="Create with AI"
                 icon="pi pi-sparkles"
                 @click="handleCreateWithAI"
               />
               <Button
                 label="View Examples"
                 outlined
                 @click="handleViewExamples"
               />
             </div>
           </template>
         </Card>
       </div>
     </div>

     <!-- Dialogs -->
     <ConfirmDialog />
     <Toast />
   </template>
   ```

7. Create App Dialog Component (Optional - inline for simplicity):
   You can create the dialog inline or as a separate component. For now, use PrimeVue's dynamic dialog.

Deliverables:
- [ ] Apps load from API on mount (GET /api/apps)
- [ ] Apps display in card grid layout
- [ ] Each card shows:
  - App icon (🌐) and name
  - Description (2 lines, truncated)
  - Status badge (Published/Draft)
  - File count and step count
  - Last updated time (relative format)
  - Edit button (navigates to editor)
  - View button (for published apps, opens in new tab)
  - Publish button (for draft apps)
  - More menu (⋮) with Duplicate, Unpublish (if published), Delete
- [ ] Search filters apps by name/description
- [ ] Status dropdown filters by All/Published/Draft
- [ ] [+ New App] button opens create dialog
- [ ] Create dialog creates app and navigates to editor
- [ ] Delete confirmation dialog works
- [ ] Unpublish confirmation dialog works
- [ ] Duplicate creates copy and navigates to editor
- [ ] Publish/Unpublish updates app status
- [ ] Quick Start section shows when no apps exist
- [ ] Empty state shows when search has no results
- [ ] Loading state shows skeletons
- [ ] Error state shows with retry button
- [ ] Responsive layout works (mobile/tablet/desktop)
- [ ] Dark mode works
- [ ] No console errors
- [ ] No TypeScript errors

Testing Checklist:
- [ ] Test initial page load (should show loading, then apps)
- [ ] Test with no apps (empty state + quick start)
- [ ] Test with API error (error message + retry)
- [ ] Test search functionality (filters as you type)
- [ ] Test status filter (All, Published, Draft)
- [ ] Test app card displays all information correctly
- [ ] Test [Edit] button navigates to /apps/{id}/edit
- [ ] Test [View] button opens /web/{slug} in new tab (published apps only)
- [ ] Test [Publish] button (draft app becomes published)
- [ ] Test [Unpublish] with confirmation (published app becomes draft)
- [ ] Test [Duplicate] creates copy and navigates to editor
- [ ] Test [Delete] with confirmation removes app from list
- [ ] Test [+ New App] button opens create dialog
- [ ] Test create dialog:
  - Name field required
  - Description optional
  - Creates app and navigates to editor
  - Shows error if creation fails
- [ ] Test Quick Start section:
  - [Create with AI] button (implement navigation)
  - [View Examples] button (implement navigation)
- [ ] Test empty search results (shows empty state message)
- [ ] Test responsive on mobile (375px), tablet (768px), desktop (1440px)
- [ ] Test dark mode toggle
- [ ] Verify cleanup on unmount
- [ ] Verify no TypeScript errors
- [ ] Verify no console warnings/errors

API Integration Notes:
- Review existing Blazor API endpoints:
  - GET /api/apps - List all apps (with optional projectId query param)
  - GET /api/apps/{id} - Get single app
  - POST /api/apps - Create app
  - PUT /api/apps/{id} - Update app
  - DELETE /api/apps/{id} - Delete app
  - POST /api/apps/{id}/publish - Publish app
  - POST /api/apps/{id}/unpublish - Unpublish app (verify this exists)
  - POST /api/apps/{id}/duplicate - Duplicate app (verify this exists)
- Check AppsController.cs for exact endpoint contracts
- Axios instance from Phase 2.1 already includes auth headers
- Handle 401 errors (redirect to login)
- Handle 403 errors (insufficient permissions)
- Handle 404 errors (app not found)

UI/UX Notes:
- Card hover effect adds subtle shadow
- Published apps show green ✅ badge
- Draft apps show gray 📝 badge
- Relative time formatting: "2h ago", "3d ago", "1w ago"
- File/step counts: "3 files, 2 steps" or "1 file" or "No files"
- Description truncates to 2 lines with ellipsis
- Grid: 1 column mobile, 2 columns tablet, 3 columns desktop
- Empty state is friendly and actionable
- Confirmation dialogs prevent accidental deletes
- Toast notifications for success/error feedback

CRITICAL: This is the apps gallery - make it visually appealing and easy to browse!
```

---

## Implementation Checklist

### Setup
- [ ] Verify Phase 1.2 Apps List stub exists
- [ ] Verify Phase 2.1 authentication is complete
- [ ] Review AppsController.cs for available endpoints

### Files to Create
- [ ] features/apps/types/apps.types.ts
- [ ] features/apps/services/apps.service.ts
- [ ] features/apps/stores/apps.store.ts
- [ ] features/apps/components/AppCard.vue

### Files to Modify
- [ ] features/apps/views/AppListView.vue (replace stub)

### Backend Verification
- [ ] Verify GET /api/apps endpoint
- [ ] Verify POST /api/apps endpoint
- [ ] Verify DELETE /api/apps/{id} endpoint
- [ ] Verify POST /api/apps/{id}/publish endpoint
- [ ] Verify POST /api/apps/{id}/unpublish or PUT endpoint
- [ ] Verify POST /api/apps/{id}/duplicate endpoint (may need to add)

### Testing
- [ ] All deliverables checked
- [ ] All test cases passed
- [ ] No TypeScript errors
- [ ] No console errors
- [ ] Dark mode works
- [ ] Responsive works

---

## Notes

- Apps List is PART 1 of Apps feature (Editor is separate)
- Focus on browsing, searching, and managing apps
- Card-based gallery design for visual appeal
- Status badges and metadata help users find apps quickly
- Quick actions (Edit, View, Publish, Delete) are one click away
- Quick Start section helps onboard new users
- Search and filters help users with many apps
