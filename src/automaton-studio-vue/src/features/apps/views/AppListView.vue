<script setup lang="ts">
import { ref, onUnmounted, watch, computed } from 'vue'
import { useRouter } from 'vue-router'
import { useToast } from 'primevue/usetoast'
import Card from 'primevue/card'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Tag from 'primevue/tag'
import Dropdown from 'primevue/dropdown'
import Skeleton from 'primevue/skeleton'
import Dialog from 'primevue/dialog'
import SelectButton from 'primevue/selectbutton'
import IconField from 'primevue/iconfield'
import InputIcon from 'primevue/inputicon'
import TabMenu from 'primevue/tabmenu'
import AppCard from '../components/AppCard.vue'
import PublishDialog from '../components/PublishDialog.vue'
import { useAppsStore } from '../stores/apps.store'
import { useAuthStore } from '@/features/auth/stores/auth.store'
import { useProjectsStore } from '@/features/projects/stores/projectsStore'
import { getPublicStoreUrl } from '@/shared/utils/public-store-url'
import type { App, PublishAppRequest } from '../types/apps.types'
import { appsService } from '../services/apps.service'
import { getPublishedAppUrl } from '../utils/public-app-url'

const publicDocsUrl = 'https://automaton.monster/docs'
const publicCliDocsUrl = `${publicDocsUrl}/automaton-cli`
const apiBase = import.meta.env.VITE_API_BASE_URL || ''

const router = useRouter()
const toast = useToast()
const appsStore = useAppsStore()
const authStore = useAuthStore()
const projectsStore = useProjectsStore()

function openExternal(url: string) {
  window.open(url, '_blank', 'noopener')
}

// View mode state
const viewMode = ref('grid')
const viewOptions = ref([
  { icon: 'pi pi-th-large', value: 'grid' },
  { icon: 'pi pi-list', value: 'list' },
])

// Tab state
const activeTabIndex = ref(0)
const tabItems = ref([
  { label: 'My Apps', icon: 'pi pi-th-large' },
  { label: 'Templates', icon: 'pi pi-star' },
  { label: 'How to Build', icon: 'pi pi-book' },
])

// Templates state
const templates = ref<App[]>([])
const isLoadingTemplates = ref(false)
const isForkingId = ref<string | null>(null)

const fetchTemplates = async () => {
  isLoadingTemplates.value = true
  try {
    templates.value = await appsService.getTemplates()
  } catch {
    // silence — templates just won't show
  } finally {
    isLoadingTemplates.value = false
  }
}

const handleUseTemplate = async (template: App) => {
  isForkingId.value = template.id
  try {
    const newApp = await appsStore.forkTemplate(template.id)
    toast.add({
      severity: 'success',
      summary: 'Template Remixed',
      detail: `"${newApp.name}" has been added to your apps`,
      life: 4000,
    })
    activeTabIndex.value = 0
    router.push(`/apps/${newApp.id}`)
  } catch (err: unknown) {
    const message = err instanceof Error ? err.message : 'Failed to use template'
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: message,
      life: 5000,
    })
  } finally {
    isForkingId.value = null
  }
}

watch(activeTabIndex, (index) => {
  if (index === 1 && templates.value.length === 0) {
    fetchTemplates()
  }
})

// Local state
const searchQuery = ref('')
const selectedStatus = ref<'all' | 'published' | 'draft'>('all')
const selectedRunner = ref<string | null>(null)
const unassignedRunnerFilter = '__unassigned__'
const runnerOptions = computed(() => [
  { label: 'All Runners', value: null },
  { label: 'Unassigned', value: unassignedRunnerFilter },
  ...Array.from(new Map(appsStore.listApps.map((app) => [app.runner?.id, app.runner?.name || 'No Runner'])))
    .filter(([id]) => id)
    .map(([value, label]) => ({ label, value })),
])

const statusOptions = ref([
  { label: 'All', value: 'all' },
  { label: 'Published', value: 'published' },
  { label: 'Draft', value: 'draft' },
])

// Quick-start how-to tab

// Create dialog state
const showCreateDialog = ref(false)
const newAppName = ref('')
const selectedRunnerId = ref<string | null>(null)
const isCreating = ref(false)
const importInput = ref<HTMLInputElement | null>(null)
const isImporting = ref(false)

// Publish dialog state
const showPublishDialog = ref(false)
const publishingApp = ref<App | null>(null)

// Computed
const showEmptySearch = computed(() => {
  return (
    appsStore.filteredApps.length === 0 &&
    appsStore.hasApps &&
    (searchQuery.value || selectedStatus.value !== 'all')
  )
})

// Watch for filter changes
watch(searchQuery, (value) => {
  appsStore.setSearchFilter(value)
})

watch(selectedStatus, (value) => {
  appsStore.setStatusFilter(value)
})

const visibleApps = computed(() => selectedRunner.value
  ? selectedRunner.value === unassignedRunnerFilter
    ? appsStore.filteredApps.filter((app) => !app.runner)
    : appsStore.filteredApps.filter((app) => app.runner?.id === selectedRunner.value)
  : appsStore.filteredApps)

// React to active project changes (including initial load)
watch(
  () => projectsStore.activeProjectId,
  async (projectId) => {
    selectedRunner.value = null
    try {
      await appsStore.fetchApps(projectId ?? undefined)
    } catch {
      // Error handled in store
    }
  },
  { immediate: true },
)

onUnmounted(() => {
  appsStore.clearFilters()
})

// Handlers
const openCreateDialog = () => {
  newAppName.value = ''
  selectedRunnerId.value = null
  showCreateDialog.value = true
}

const handleCreateApp = async () => {
  if (!newAppName.value.trim()) return
  isCreating.value = true
  try {
    const app = await appsStore.createApp({ name: newAppName.value.trim(), executionRunnerId: selectedRunnerId.value ?? undefined })
    showCreateDialog.value = false
    router.push(`/apps/${app.id}`)
  } finally {
    isCreating.value = false
  }
}

const openImportDialog = () => {
  importInput.value?.click()
}

const handleImportSelected = async (event: Event) => {
  const input = event.target as HTMLInputElement | null
  const file = input?.files?.[0]
  if (!file) {
    return
  }

  isImporting.value = true
  try {
    const app = await appsStore.importApp(file)
    toast.add({
      severity: 'success',
      summary: 'Imported',
      detail: `App "${app.name}" imported successfully`,
      life: 4000,
    })
    router.push(`/apps/${app.id}`)
  } catch (err: unknown) {
    const message = err instanceof Error ? err.message : 'Failed to import app'
    toast.add({
      severity: 'error',
      summary: 'Import Failed',
      detail: message,
      life: 5000,
    })
  } finally {
    isImporting.value = false
    if (input) {
      input.value = ''
    }
  }
}

const viewPublicApps = () => {
  if (authStore.publicUsername) {
    window.open(getPublicStoreUrl(authStore.publicUsername), '_blank', 'noopener')
  } else {
    toast.add({
      severity: 'warn',
      summary: 'Profile not set',
      detail: 'Set your public username in Settings to view your public apps.',
      life: 4000,
    })
    router.push('/settings')
  }
}

const openPublishDialog = async (appId: string) => {
  publishingApp.value = await appsStore.fetchApp(appId)
  showPublishDialog.value = true
}

const handlePublishApp = async (request: PublishAppRequest) => {
  if (!publishingApp.value) return

  try {
    await appsStore.publishApp(publishingApp.value.id, request)
    toast.add({
      severity: 'success',
      summary: 'Published',
      detail: `App is now available at ${getPublishedAppUrl(request.publicUrl)}`,
      life: 5000,
    })
    showPublishDialog.value = false
    publishingApp.value = null
  } catch (err: unknown) {
    const message = err instanceof Error ? err.message : 'Failed to publish app'
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: message,
      life: 5000,
    })
  }
}

const handleRetry = async () => {
  appsStore.clearError()
  try {
    await appsStore.fetchApps()
  } catch {
    // Error is handled in the store
  }
}
</script>

<template>
  <div class="apps-view">
    <input
      ref="importInput"
      type="file"
      accept=".zip,application/zip"
      class="hidden"
      @change="handleImportSelected"
    />

    <!-- Page Header -->
    <div class="flex align-items-center justify-content-between mb-5">
      <div>
        <h1 class="text-3xl font-bold m-0">Apps</h1>
        <p class="text-color-secondary mt-1 mb-0">Build and publish web applications</p>
      </div>
      <div class="flex gap-2">
        <Button label="Create App" icon="pi pi-plus" @click="openCreateDialog" />
        <Button
          label="Import App"
          icon="pi pi-upload"
          outlined
          :loading="isImporting"
          @click="openImportDialog"
        />
        <Button
          label="View Public Apps"
          icon="pi pi-external-link"
          outlined
          @click="viewPublicApps"
        />
      </div>
    </div>

    <!-- Tabs: My Apps / Templates -->
    <TabMenu v-model:activeIndex="activeTabIndex" :model="tabItems" class="mb-4" />

    <!-- ─── MY APPS TAB ────────────────────────────────────────────── -->
    <template v-if="activeTabIndex === 0">

    <!-- Apps List Header -->
    <div class="flex align-items-center justify-content-between mb-4">
      <div class="flex align-items-center gap-3">
        <span class="font-semibold">Your Apps</span>
        <Tag :value="`${appsStore.filteredApps.length} apps`" severity="secondary" />
      </div>
      <div class="flex align-items-center gap-3">
        <IconField>
          <InputIcon class="pi pi-search" />
          <InputText v-model="searchQuery" placeholder="Search apps..." />
        </IconField>
        <Dropdown
          v-model="selectedStatus"
          :options="statusOptions"
          option-label="label"
          option-value="value"
          placeholder="Status"
          class="w-10rem"
        />
        <Dropdown
          v-model="selectedRunner"
          :options="runnerOptions"
          option-label="label"
          option-value="value"
          placeholder="Runner"
          class="w-12rem"
        />
        <SelectButton v-model="viewMode" :options="viewOptions" option-value="value">
          <template #option="{ option }">
            <i :class="option.icon"></i>
          </template>
        </SelectButton>
      </div>
    </div>

    <!-- Loading State -->
    <div v-if="appsStore.isLoading">
      <div class="grid">
        <div v-for="i in 6" :key="i" class="col-12 md:col-6 lg:col-4">
          <Card class="h-full">
            <template #content>
              <div class="flex flex-column gap-3">
                <div class="flex align-items-center gap-2">
                  <Skeleton shape="circle" size="1.5rem" />
                  <Skeleton width="60%" height="1.25rem" />
                </div>
                <Skeleton width="100%" height="2.5rem" />
                <Skeleton width="5rem" height="1.5rem" border-radius="1rem" />
                <Skeleton width="8rem" height="1rem" />
                <div class="flex gap-2 mt-2">
                  <Skeleton width="50%" height="2rem" />
                  <Skeleton width="50%" height="2rem" />
                </div>
              </div>
            </template>
          </Card>
        </div>
      </div>
    </div>

    <!-- Error State -->
    <Message v-else-if="appsStore.error" severity="error" :closable="false">
      <div class="flex align-items-center justify-content-between w-full">
        <span>{{ appsStore.error }}</span>
        <Button label="Retry" severity="danger" size="small" outlined @click="handleRetry" />
      </div>
    </Message>

    <!-- Content -->
    <template v-else>
      <!-- Apps Grid -->
      <div v-if="visibleApps.length > 0" :class="viewMode === 'list' ? 'app-list' : 'grid'">
        <div
          v-for="app in visibleApps"
          :key="app.id"
          class="col-12 md:col-6 lg:col-4"
        >
          <AppCard v-if="viewMode === 'grid'" :app="app" @publish="openPublishDialog" />
          <div v-else class="app-list-row flex align-items-center gap-3 p-3 border-1 surface-border border-round">
            <i class="pi pi-globe text-primary text-xl" />
            <div class="flex-1 min-w-0">
              <div class="font-semibold text-overflow-ellipsis overflow-hidden white-space-nowrap">{{ app.name }}</div>
              <div class="text-color-secondary text-sm">{{ app.runner?.name || 'Unassigned' }} · {{ app.runner?.connectivityStatus || 'None' }}</div>
            </div>
            <div class="text-sm text-color-secondary">{{ app.runtime.frontendAvailable ? 'Frontend' : 'No frontend' }} · {{ app.runtime.backendAvailable ? 'Backend' : 'No backend' }}</div>
            <Button label="Edit" icon="pi pi-pencil" size="small" outlined @click="router.push(`/apps/${app.id}`)" />
          </div>
        </div>
      </div>

      <!-- Empty Search Results -->
      <Card v-else-if="showEmptySearch">
        <template #content>
          <div class="text-center py-5">
            <i class="pi pi-search text-5xl text-color-secondary mb-4"></i>
            <h3 class="text-xl font-semibold mb-2">No apps found</h3>
            <p class="text-color-secondary mb-4">Try adjusting your search or filters</p>
            <Button
              label="Clear Filters"
              icon="pi pi-filter-slash"
              outlined
              @click="
                () => {
                  searchQuery = ''
                  selectedStatus = 'all'
                }
              "
            />
          </div>
        </template>
      </Card>

      <!-- Empty State (No Apps) -->
      <Card v-else-if="!appsStore.hasApps">
        <template #content>
          <div class="text-center py-5">
            <i class="pi pi-globe text-6xl text-color-secondary mb-4"></i>
            <h3 class="text-xl font-semibold mb-2">No apps yet</h3>
            <p class="text-color-secondary mb-4">
              Create an app from scratch, import an existing project, or start from a template.
            </p>
            <Button label="Create App" icon="pi pi-plus" @click="openCreateDialog" />
          </div>
        </template>
      </Card>

    </template><!-- /v-else -->
    </template><!-- /v-if="activeTabIndex === 0" -->

    <!-- ─── HOW TO BUILD TAB ─────────────────────────────────────────── -->
    <template v-if="activeTabIndex === 2">
      <Card class="quick-start-card">
        <template #content>
          <div class="quick-start-header mb-3">
            <i class="pi pi-book text-xl" style="color: var(--p-primary-500)" />
            <span class="font-semibold ml-2">How to build an app</span>
          </div>

          <div class="qs-content">
            <p class="text-color-secondary text-sm mt-0 mb-3">
              Build locally with any editor (Cursor, VS Code…), keep the project in Git,
              and deploy with the CLI. Let the CLI create or link the project that it will deploy.
            </p>
            <div class="qs-code mb-2">dotnet tool install -g Automaton.Cli</div>
            <div class="qs-code mb-2">automaton login</div>
            <div class="qs-code mb-2">automaton new "My App"</div>
            <div class="qs-code mb-2">automaton validate</div>
            <div class="qs-code mb-3">automaton deploy</div>
            <p class="text-color-secondary text-sm mb-2">
              Automaton apps need to follow a specific project structure, backend controller pattern,
              and frontend integration contract. Point your AI tool to
              <a href="https://automaton.monster/llms.txt" target="_blank" rel="noopener" class="qs-link">llms.txt</a>
              before it starts generating files.
            </p>
            <div class="qs-links mb-3">
              <Button
                label="CLI Reference"
                icon="pi pi-book"
                size="small"
                severity="secondary"
                outlined
                @click="openExternal(publicCliDocsUrl)"
              />
              <Button
                label="Open Studio documentation"
                icon="pi pi-book"
                size="small"
                severity="secondary"
                outlined
                @click="openExternal(publicDocsUrl)"
              />
            </div>
            <p class="text-color-secondary text-sm mb-2">
              Use <a href="https://automaton.monster/llms.txt" target="_blank" rel="noopener" class="qs-link">llms.txt</a>
              when you want the exact structure and AI instructions. Use
              <button type="button" class="qs-inline-link" @click="openExternal(publicDocsUrl)">Studio documentation</button>
              when you want a more guided walkthrough with quick starts and examples.
            </p>
          </div>

        </template>
      </Card>
    </template><!-- /v-if="activeTabIndex === 2" -->

    <!-- ─── TEMPLATES TAB ────────────────────────────────────────────── -->
    <template v-if="activeTabIndex === 1">
      <!-- Loading -->
      <div v-if="isLoadingTemplates" class="grid">
        <div v-for="i in 6" :key="i" class="col-12 md:col-6 lg:col-4">
          <Card class="h-full">
            <template #content>
              <div class="flex flex-column gap-3">
                <Skeleton width="70%" height="1.5rem" />
                <Skeleton width="100%" height="3rem" />
                <Skeleton width="5rem" height="1.5rem" border-radius="1rem" />
                <Skeleton width="100%" height="2.5rem" />
              </div>
            </template>
          </Card>
        </div>
      </div>

      <!-- Empty -->
      <Card v-else-if="templates.length === 0">
        <template #content>
          <div class="text-center py-5">
            <i class="pi pi-star text-6xl text-color-secondary mb-4"></i>
            <h3 class="text-xl font-semibold mb-2">No templates yet</h3>
            <p class="text-color-secondary mb-0">
              Published template apps will appear here so you can remix them into your account.
            </p>
          </div>
        </template>
      </Card>

      <!-- Template Cards -->
      <div v-else class="grid">
        <div
          v-for="template in templates"
          :key="template.id"
          class="col-12 md:col-6 lg:col-4"
        >
          <Card class="template-card h-full">
            <template #header>
              <div class="template-thumbnail">
                <img
                  v-if="template.thumbnailUrl"
                  :src="template.thumbnailUrl.startsWith('http') ? template.thumbnailUrl : `${apiBase}${template.thumbnailUrl}`"
                  :alt="template.name"
                  loading="lazy"
                />
                <div v-else class="template-thumbnail-placeholder flex align-items-center justify-content-center">
                  <i class="pi pi-globe text-4xl text-color-secondary"></i>
                </div>
                <Tag v-if="template.isFeatured" value="Featured" severity="warn" class="featured-badge" />
              </div>
            </template>
            <template #title>{{ template.name }}</template>
            <template #subtitle>
              <Tag v-if="template.category" :value="template.category" severity="secondary" class="text-xs mr-1" />
              <span class="text-color-secondary text-sm">{{ template.viewCount }} views</span>
            </template>
            <template #content>
              <p v-if="template.description" class="text-color-secondary text-sm mt-0 mb-3 line-clamp-2">
                {{ template.description }}
              </p>
              <p v-else-if="template.summary" class="text-color-secondary text-sm mt-0 mb-3 line-clamp-2">
                {{ template.summary }}
              </p>
              <Button
                label="Use Template"
                icon="pi pi-copy"
                class="w-full"
                :loading="isForkingId === template.id"
                :disabled="isForkingId !== null"
                @click="handleUseTemplate(template)"
              />
            </template>
          </Card>
        </div>
      </div>
    </template><!-- /v-if="activeTabIndex === 1" -->

    <!-- Create App Dialog -->
    <Dialog
      v-model:visible="showCreateDialog"
      modal
      header="Create New App"
      :style="{ width: '450px' }"
      :closable="!isCreating"
      :close-on-escape="!isCreating"
    >
      <div class="flex flex-column gap-3">
        <div class="flex flex-column gap-2">
          <label for="app-name" class="font-semibold">App Name</label>
          <InputText
            id="app-name"
            v-model="newAppName"
            placeholder="Enter app name"
            class="w-full"
            :disabled="isCreating"
            autofocus
            @keyup.enter="handleCreateApp"
          />
        </div>
      </div>
      <template #footer>
        <div class="flex justify-content-end gap-2">
          <Button
            label="Cancel"
            severity="secondary"
            outlined
            :disabled="isCreating"
            @click="showCreateDialog = false"
          />
          <Button label="Create" icon="pi pi-plus" :loading="isCreating" @click="handleCreateApp" />
        </div>
      </template>
    </Dialog>

    <PublishDialog
      v-model:visible="showPublishDialog"
      :current-slug="publishingApp?.publicUrl"
      :current-category="publishingApp?.category"
      :current-tags="publishingApp?.tags"
      :requires-auth="publishingApp?.requiresAuthentication"
      :is-forkable="publishingApp?.isForkable"
      :app-name="publishingApp?.name"
      @publish="handlePublishApp"
    />
  </div>
</template>

<style scoped>
.apps-view {
  max-width: 1400px;
  margin: 0 auto;
}

.quick-start-card {
  /* stretch to fill available width — no max-width constraint */
}

.quick-start-header {
  display: flex;
  align-items: center;
}

/* ── Quick-start tabs ── */
.qs-tabs {
  display: flex;
  gap: 8px;
  flex-wrap: wrap;
}

.qs-tab {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 7px 14px;
  border-radius: 6px;
  border: 1px solid var(--p-surface-border);
  background: transparent;
  color: var(--p-text-color);
  font-size: 0.82rem;
  font-weight: 500;
  cursor: pointer;
  transition: all 0.15s;
}

.qs-tab:hover {
  border-color: var(--p-primary-500);
  color: var(--p-primary-500);
}

.qs-tab--active {
  border-color: var(--p-primary-500);
  background: color-mix(in srgb, var(--p-primary-500) 10%, transparent);
  color: var(--p-primary-500);
}

/* ── Quick-start content ── */
.qs-content {
  /* no width cap — let it breathe inside the full-width card */
}

.qs-steps {
  color: var(--p-text-muted-color);
  font-size: 0.86rem;
  line-height: 1.7;
  margin: 0 0 12px;
  padding-left: 20px;
}

.qs-steps li { margin-bottom: 4px; }

/* Code blocks are always dark regardless of theme — same as every IDE/terminal */
.qs-code {
  background: #161b22;
  border: 1px solid #30363d;
  border-radius: 6px;
  padding: 8px 12px;
  font-family: var(--font-mono, monospace);
  font-size: 0.8rem;
  color: #8ef0a1;
  overflow-x: auto;
}

.qs-code--block {
  color: #e6edf3;
  line-height: 1.55;
  font-size: 0.78rem;
}

.qs-code--block em {
  color: #ffa657;
  font-style: normal;
}

.qs-inline-code {
  background: color-mix(in srgb, var(--p-primary-500) 12%, var(--p-surface-ground));
  color: var(--p-primary-400);
  padding: 1px 5px;
  border-radius: 4px;
  font-family: var(--font-mono, monospace);
  font-size: 0.83em;
}

.qs-links {
  display: flex;
  gap: 0.75rem;
  flex-wrap: wrap;
}

.qs-link,
.qs-inline-link {
  color: var(--p-primary-500);
  font-weight: 600;
}

.qs-link:hover,
.qs-inline-link:hover {
  color: var(--p-primary-400);
}

.qs-inline-link {
  background: none;
  border: 0;
  padding: 0;
  cursor: pointer;
  font: inherit;
  text-decoration: underline;
}

.template-card .template-thumbnail {
  position: relative;
  height: 160px;
  overflow: hidden;
  border-radius: var(--p-card-border-radius, 8px) var(--p-card-border-radius, 8px) 0 0;
}

.template-card .template-thumbnail img {
  width: 100%;
  height: 100%;
  object-fit: cover;
}

.template-card .template-thumbnail-placeholder {
  width: 100%;
  height: 100%;
  background: var(--p-surface-100);
}

.featured-badge {
  position: absolute;
  top: 8px;
  right: 8px;
}

.line-clamp-2 {
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
}
</style>



