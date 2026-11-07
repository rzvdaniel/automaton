<script setup lang="ts">
import { ref, onMounted, onUnmounted, watch, computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import Card from 'primevue/card'
import Button from 'primevue/button'
import Message from 'primevue/message'
import Skeleton from 'primevue/skeleton'
import TabMenu from 'primevue/tabmenu'
import AppRow from '../components/AppRow.vue'
import CopyCommand from '../components/CopyCommand.vue'
import CatalogPanel from '../components/CatalogPanel.vue'
import SharedAppsSection from '../components/SharedAppsSection.vue'
import { organizationsService, type OrganizationAppSummary } from '@/features/organizations/services/organizationsService'
import { useAppsStore } from '../stores/apps.store'
import {
  appAccess, appDescription, appState, matchesFilter, matchesSearch, type AppFilter,
} from '../utils/app-list-display'

const publicDocsUrl = 'https://automaton.monster/docs'
const publicCliDocsUrl = `${publicDocsUrl}/automaton-cli`
const router = useRouter()
const route = useRoute()
const appsStore = useAppsStore()

function openExternal(url: string) {
  window.open(url, '_blank', 'noopener')
}

// Tab state: the Apps list, the Catalog and the local setup guide ("New app").
const activeTabIndex = ref(route.meta.appsTab === 'catalog' ? 1 : 0)
const tabItems = ref([
  { label: 'My Apps', icon: 'pi pi-th-large' },
  { label: 'Catalog', icon: 'pi pi-download' },
  { label: 'Local setup', icon: 'pi pi-book' },
])

watch(() => route.meta.appsTab, (tab) => { activeTabIndex.value = tab === 'catalog' ? 1 : 0 })

// Search and filter pills
const searchQuery = ref('')
const filter = ref<AppFilter>('all')

const searched = computed(() => appsStore.listApps.filter((app) => matchesSearch(app, searchQuery.value)))
const visibleApps = computed(() => searched.value.filter((app) => matchesFilter(app, filter.value)))
const filterPills = computed(() => [
  { value: 'all' as const, label: `All · ${appsStore.listApps.length}` },
  { value: 'web' as const, label: 'Web apps' },
  { value: 'actions' as const, label: 'Actions' },
  { value: 'stopped' as const, label: 'Stopped' },
])

// Apps other people shared with the user (organization assignments), minus the ones the user owns.
const sharedApps = ref<OrganizationAppSummary[]>([])

async function loadSharedApps() {
  try {
    const assigned = await organizationsService.getMyAssignedApps()
    const ownIds = new Set(appsStore.listApps.map((app) => app.id))
    sharedApps.value = assigned.filter((app) => !ownIds.has(app.id))
  } catch {
    sharedApps.value = []
  }
}

onMounted(async () => {
  try {
    await appsStore.fetchApps()
  } catch {
    // Error is handled in the store
  }
  await loadSharedApps()
})

onUnmounted(() => {
  appsStore.clearFilters()
})

const goToCatalog = () => router.push({ name: 'apps-catalog' })
const newApp = () => { activeTabIndex.value = 2 }

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
    <TabMenu v-if="activeTabIndex !== 0" v-model:active-index="activeTabIndex" :model="tabItems" class="mb-4" />

    <!-- ─── MY APPS TAB ────────────────────────────────────────────── -->
    <template v-if="activeTabIndex === 0">
      <header class="apps-head">
        <div>
          <h1 class="apps-head__title">Apps</h1>
          <p class="apps-head__line">Everything running on your server. Ask your AI to add one, or install from the catalog.</p>
        </div>
        <div class="apps-head__buttons">
          <Button label="From the catalog" outlined class="btn-outline" @click="goToCatalog" />
          <Button label="New app" class="btn-ink" @click="newApp" />
        </div>
      </header>

      <div v-if="appsStore.hasApps" class="apps-toolbar">
        <label class="apps-search">
          <span class="sr-only">Search apps</span>
          <i class="pi pi-search" aria-hidden="true" />
          <input v-model="searchQuery" type="search" placeholder="Search apps" />
        </label>
        <div class="apps-pills" role="group" aria-label="Filter apps">
          <button
            v-for="pill in filterPills"
            :key="pill.value"
            type="button"
            class="apps-pill"
            :class="{ 'apps-pill--active': filter === pill.value }"
            :aria-pressed="filter === pill.value"
            @click="filter = pill.value"
          >{{ pill.label }}</button>
        </div>
      </div>

      <div v-if="appsStore.isLoading" class="apps-card" aria-busy="true">
        <Skeleton v-for="i in 4" :key="i" height="68px" border-radius="12px" />
      </div>

      <Message v-else-if="appsStore.error" severity="error" :closable="false">
        <div class="flex align-items-center justify-content-between w-full">
          <span>{{ appsStore.error }}</span>
          <Button label="Retry" severity="danger" size="small" outlined @click="handleRetry" />
        </div>
      </Message>

      <template v-else>
        <div v-if="visibleApps.length > 0" class="apps-card">
          <AppRow
            v-for="app in visibleApps"
            :id="app.id"
            :key="app.id"
            :to="`/apps/${app.id}`"
            :name="app.name"
            :line="appDescription(app)"
            :image-url="app.thumbnailUrl"
            :access="appAccess(app)"
            :state="appState(app)"
          />
        </div>

        <div v-else-if="appsStore.hasApps" class="apps-card apps-empty">
          <p class="apps-empty__title">No apps match</p>
          <p class="apps-empty__line">Try another search or filter.</p>
          <Button label="Clear filters" outlined class="btn-outline" @click="searchQuery = ''; filter = 'all'" />
        </div>

        <div v-else-if="sharedApps.length === 0" class="apps-card apps-empty">
          <p class="apps-empty__title">No apps yet</p>
          <p class="apps-empty__line">Ask your AI to add one, or install a ready-made app from the catalog.</p>
          <div class="apps-head__buttons">
            <Button label="From the catalog" outlined class="btn-outline" @click="goToCatalog" />
            <Button label="New app" class="btn-ink" @click="newApp" />
          </div>
        </div>

        <SharedAppsSection :apps="sharedApps" />
      </template>
    </template>

    <!-- ─── CATALOG TAB ──────────────────────────────────────────────── -->
    <CatalogPanel v-if="activeTabIndex === 1" />

    <!-- ─── LOCAL SETUP TAB ──────────────────────────────────────────── -->
    <template v-if="activeTabIndex === 2">
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
    </template><!-- /v-if="activeTabIndex === 2" -->

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
    width: 100% !important; /* beats the fixed w-10rem / w-12rem widths */
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

<style scoped>
.apps-head { display: flex; flex-wrap: wrap; align-items: flex-start; justify-content: space-between; gap: 16px; margin-bottom: 20px; }
.apps-head__title { margin: 0; font-family: var(--display); font-size: 28px; font-weight: 700; color: var(--ink); }
.apps-head__line { margin: 4px 0 0; color: var(--ink-soft); font-size: 15px; max-width: 52ch; }
.apps-head__buttons { display: flex; flex-wrap: wrap; gap: 8px; }
.btn-ink.p-button { min-height: 44px; background: var(--ink); border-color: var(--ink); color: var(--paper); border-radius: 999px; font-weight: 600; }
.btn-ink.p-button:hover { background: var(--ink); border-color: var(--signal); color: var(--paper); }
.btn-outline.p-button { min-height: 44px; background: transparent; border: 1px solid var(--line); color: var(--ink); border-radius: 999px; font-weight: 600; }
.apps-toolbar { display: flex; flex-wrap: wrap; align-items: center; gap: 12px; margin-bottom: 16px; }
.apps-search { position: relative; flex: 1 1 240px; max-width: 360px; }
.apps-search i { position: absolute; left: 12px; top: 50%; transform: translateY(-50%); color: var(--ink-soft); }
.apps-search input {
  width: 100%; min-height: 44px; padding: 8px 12px 8px 34px; box-sizing: border-box;
  border: 1px solid var(--line); border-radius: 999px; background: var(--paper); color: var(--ink); font: inherit; font-size: 14px;
}
.apps-search input:focus-visible { outline: 2px solid var(--signal); outline-offset: 2px; }
.sr-only { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); white-space: nowrap; }
.apps-pills { display: flex; flex-wrap: wrap; gap: 8px; }
.apps-pill {
  min-height: 44px; padding: 6px 16px; border: 1px solid var(--line); border-radius: 999px;
  background: transparent; color: var(--ink); font: inherit; font-size: 14px; cursor: pointer;
}
.apps-pill--active { background: var(--ink); border-color: var(--ink); color: var(--paper); }
.apps-pill:focus-visible { outline: 2px solid var(--signal); outline-offset: 2px; }
.apps-card {
  display: flex; flex-direction: column; gap: 8px; padding: 16px;
  background: var(--paper); border: 1px solid var(--line); border-radius: 18px;
}
.apps-empty { align-items: center; text-align: center; padding: 40px 16px; }
.apps-empty__title { margin: 0; font-family: var(--display); font-size: 20px; font-weight: 600; }
.apps-empty__line { margin: 0 0 8px; color: var(--ink-soft); }
</style>
