<script setup lang="ts">
import { ref, onUnmounted, watch, computed } from 'vue'
import { useRouter } from 'vue-router'
import { useToast } from 'primevue/usetoast'
import Card from 'primevue/card'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Tag from 'primevue/tag'
import Dropdown from 'primevue/dropdown'
import MultiSelect from 'primevue/multiselect'
import Skeleton from 'primevue/skeleton'
import SelectButton from 'primevue/selectbutton'
import IconField from 'primevue/iconfield'
import InputIcon from 'primevue/inputicon'
import TabMenu from 'primevue/tabmenu'
import AppCard from '../components/AppCard.vue'
import CopyCommand from '../components/CopyCommand.vue'
import { useAppsStore } from '../stores/apps.store'
import { useAuthStore } from '@/features/auth/stores/auth.store'
import { useProjectsStore } from '@/features/projects/stores/projectsStore'
import { getPublicStoreUrl } from '@/shared/utils/public-store-url'
import type { AppListItem } from '../types/apps.types'
import { appsService } from '../services/apps.service'

const publicDocsUrl = 'https://automaton.monster/docs'
const publicCliDocsUrl = `${publicDocsUrl}/automaton-cli`
const router = useRouter()
const toast = useToast()
const appsStore = useAppsStore()
const authStore = useAuthStore()
const projectsStore = useProjectsStore()

const getDeploymentSummary = (app: AppListItem) => {
  return app.runtime.genericRuntimeDeployed ? 'Generic runtime deployed' : 'Not deployed yet'
}

const getLiveDestinationLabel = (app: AppListItem) => {
  const environment = app.liveEnvironment
  if (!environment || environment.status === 'Unassigned') return 'No runtime assigned'
  if (environment.isHostedRunner) return 'Automaton Hosted'
  return environment.applicationProfile === 'PrivilegedHost'
    ? 'Self-hosted · Privileged Host'
    : environment.applicationProfile === 'MachineManagement'
      ? 'Self-hosted · Machine Management'
    : 'Self-hosted · Standard'
}

const getLiveStatusLabel = (app: AppListItem) => {
  const environment = app.liveEnvironment
  if (!environment) return app.runner?.connectivityStatus === 'Online' ? 'Running' : 'Unassigned'
  if (!environment.runnerOnline && environment.status !== 'Unassigned') return 'Offline'
  if (!environment.genericRuntimeDeployed) return 'Unassigned'
  return environment.status === 'Running' ? 'Running' : environment.status
}

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
  { label: 'Local setup', icon: 'pi pi-book' },
])

// Local state
const searchQuery = ref('')
const selectedStatus = ref<'all' | 'published' | 'draft'>('all')
const selectedRunner = ref<string | null>(null)
const selectedTags = ref<string[]>([])
const selectedTagGroup = ref<string | null>(null)
const tagSuggestions = ref<string[]>([])
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
  { label: 'Unpublished', value: 'draft' },
])

const tagOptions = computed(() => {
  const tags = new Map<string, string>()
  for (const tag of [...tagSuggestions.value, ...appsStore.listApps.flatMap((app) => app.tags)]) {
    const key = tag.toLocaleLowerCase()
    if (!tags.has(key)) tags.set(key, tag)
  }

  return Array.from(tags.values())
    .sort((left, right) => left.localeCompare(right, undefined, { sensitivity: 'accent' }))
    .map((tag) => ({ label: tag, value: tag }))
})

// Watch for filter changes
watch(searchQuery, (value) => {
  appsStore.setSearchFilter(value)
})

watch(selectedStatus, (value) => {
  appsStore.setStatusFilter(value)
})

const hasTag = (app: AppListItem, tag: string) =>
  app.tags.some((appTag) => appTag.localeCompare(tag, undefined, { sensitivity: 'accent' }) === 0)

const visibleApps = computed(() => {
  let apps = selectedRunner.value
    ? selectedRunner.value === unassignedRunnerFilter
      ? appsStore.filteredApps.filter((app) => !app.runner)
      : appsStore.filteredApps.filter((app) => app.runner?.id === selectedRunner.value)
    : appsStore.filteredApps

  if (selectedTags.value.length) {
    apps = apps.filter((app) => selectedTags.value.some((tag) => hasTag(app, tag)))
  }

  return selectedTagGroup.value
    ? apps.filter((app) => hasTag(app, selectedTagGroup.value!))
    : apps
})

const showEmptySearch = computed(() => {
  const hasActiveFilter =
    Boolean(searchQuery.value) ||
    selectedStatus.value !== 'all' ||
    selectedRunner.value !== null ||
    selectedTags.value.length > 0 ||
    selectedTagGroup.value !== null

  return visibleApps.value.length === 0 && appsStore.hasApps && hasActiveFilter
})

// React to active project changes (including initial load)
watch(
  () => projectsStore.activeProjectId,
  async (projectId) => {
    selectedRunner.value = null
    selectedTags.value = []
    selectedTagGroup.value = null
    try {
      const [, suggestions] = await Promise.all([
        appsStore.fetchApps(projectId ?? undefined),
        appsService.getTagSuggestions(projectId ?? undefined),
      ])
      tagSuggestions.value = suggestions
    } catch {
      tagSuggestions.value = []
    }
  },
  { immediate: true },
)

onUnmounted(() => {
  appsStore.clearFilters()
})

// Handlers
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
    <!-- Page Header -->
    <div class="flex align-items-center justify-content-between mb-5">
      <div>
        <h1 class="text-3xl font-bold m-0">Apps</h1>
        <p class="text-color-secondary mt-1 mb-0">Operate and publish your applications</p>
      </div>
      <div class="flex gap-2">
        <Button
          label="View Public Apps"
          icon="pi pi-external-link"
          outlined
          @click="viewPublicApps"
        />
      </div>
    </div>

    <TabMenu v-model:active-index="activeTabIndex" :model="tabItems" class="mb-4" />

    <!-- ─── MY APPS TAB ────────────────────────────────────────────── -->
    <template v-if="activeTabIndex === 0">

    <!-- Apps List Header -->
    <div class="apps-list-header flex align-items-center justify-content-between mb-4">
      <div class="apps-list-title flex align-items-center gap-3">
        <span class="font-semibold">Your Apps</span>
        <Tag :value="`${visibleApps.length} apps`" severity="secondary" />
      </div>
      <div class="apps-list-filters flex align-items-center gap-3">
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
        <MultiSelect
          v-model="selectedTags"
          :options="tagOptions"
          option-label="label"
          option-value="value"
          placeholder="Tags"
          display="chip"
          :show-toggle-all="false"
          class="w-12rem"
        />
        <Dropdown
          v-model="selectedTagGroup"
          :options="tagOptions"
          option-label="label"
          option-value="value"
          placeholder="Group by tag"
          show-clear
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
      <div v-if="selectedTagGroup" class="text-color-secondary text-sm mb-3">
        Showing tag group: <span class="font-semibold">{{ selectedTagGroup }}</span>
      </div>
      <!-- Apps Grid -->
      <div v-if="visibleApps.length > 0" :class="viewMode === 'list' ? 'app-list' : 'grid apps-grid'">
        <div
          v-for="app in visibleApps"
          :key="app.id"
          :class="viewMode === 'grid' ? 'col-12 md:col-6 lg:col-4' : 'col-12'"
        >
          <AppCard v-if="viewMode === 'grid'" :app="app" />
          <div v-else class="app-list-row flex flex-column md:flex-row md:align-items-center gap-3 p-3 border-1 surface-border border-round">
            <i class="pi pi-globe text-primary text-xl" />
            <div class="flex-1 min-w-0">
              <div class="flex align-items-center gap-2">
                <div class="font-semibold text-overflow-ellipsis overflow-hidden white-space-nowrap">{{ app.name }}</div>
                <Tag :value="app.isPublished ? 'Published' : 'Unpublished'" :severity="app.isPublished ? 'success' : 'secondary'" class="text-xs" />
              </div>
              <div class="text-color-secondary text-sm">Live · {{ getLiveDestinationLabel(app) }} · {{ getLiveStatusLabel(app) }}</div>
              <div v-if="app.tags.length" class="flex flex-wrap gap-1 mt-1">
                <Tag v-for="tag in app.tags.slice(0, 2)" :key="tag" :value="tag" severity="secondary" class="text-xs" />
              </div>
            </div>
            <div class="text-sm text-color-secondary">{{ getDeploymentSummary(app) }}</div>
            <Button label="Manage" icon="pi pi-cog" size="small" outlined @click="router.push(`/apps/${app.id}`)" />
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
                  selectedTags = []
                  selectedTagGroup = null
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
              Create an app in a local project with Automaton CLI, then manage its deployment and runtime in Studio.
            </p>
            <Button label="View local setup" icon="pi pi-book" @click="activeTabIndex = 1" />
          </div>
        </template>
      </Card>

    </template><!-- /v-else -->
    </template><!-- /v-if="activeTabIndex === 0" -->

    <!-- ─── LOCAL SETUP TAB ──────────────────────────────────────────── -->
    <template v-if="activeTabIndex === 1">
      <Card class="quick-start-card">
        <template #content>
          <div class="quick-start-header mb-3">
            <i class="pi pi-book text-xl" style="color: var(--p-primary-500)" />
            <span class="font-semibold ml-2">Start a local project</span>
          </div>

          <div class="qs-content">
            <p class="text-color-secondary text-sm mt-0 mb-3">
              Work in a local project with the editor and AI of your choice. Run the CLI from that project folder. <code>automaton new</code> creates the local app contract and links a new app. Use <code>automaton deploy</code> to deploy source changes. Studio manages deployments, runtime, logs, and publishing.
            </p>
            <CopyCommand class="qs-code mb-2" command="dotnet tool install -g Automaton.Cli" />
            <CopyCommand class="qs-code mb-2" command="automaton login" />
            <CopyCommand class="qs-code mb-2" command='automaton new "My App"' />
            <CopyCommand class="qs-code mb-2" command="automaton validate" />
            <CopyCommand class="qs-code mb-3" command="automaton deploy" />
            <p class="text-color-secondary text-sm mb-2">
              Automaton apps use a generic application contract for build, runtime, health, storage,
              and environment settings. Point your AI tool to
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
    </template><!-- /v-if="activeTabIndex === 1" -->

  </div>
</template>

<style scoped>
.apps-view {
  max-width: 1400px;
  margin: 0 auto;
}

.apps-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(min(100%, 320px), 360px));
  justify-content: start;
  gap: 1.25rem;
}

.apps-grid > div {
  width: auto;
  padding: 0;
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

/* CLI command blocks use the same colors in both themes. */
.qs-code {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  background: #161b22;
  border: 1px solid #30363d;
  border-radius: 6px;
  padding: 8px 12px;
  font-family: var(--font-mono, monospace);
  font-size: 0.8rem;
  color: #8ef0a1;
  overflow-x: auto;
}

.qs-code :deep(.copy-command__button) {
  color: #8ef0a1;
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

@media (max-width: 767px) {
  .apps-list-header {
    align-items: flex-start;
    flex-direction: column;
    gap: 0.75rem;
  }

  .apps-list-filters {
    display: grid !important;
    grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
    width: 100%;
  }

  .apps-list-filters :deep(.p-iconfield),
  .apps-list-filters :deep(.p-inputtext),
  .apps-list-filters :deep(.p-select),
  .apps-list-filters :deep(.p-multiselect) {
    width: 100%;
  }

  .apps-list-filters :deep(.p-iconfield) {
    grid-column: 1 / -1;
  }

  .apps-list-filters :deep(.p-selectbutton) {
    grid-column: 1 / -1;
    justify-self: start;
  }
}
</style>
