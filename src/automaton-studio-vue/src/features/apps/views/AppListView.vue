<script setup lang="ts">
import { ref, onMounted, onUnmounted, watch, computed } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'
import Button from 'primevue/button'
import { useToast } from 'primevue/usetoast'
import Message from 'primevue/message'
import Skeleton from 'primevue/skeleton'
import AppFilterBar from '../components/AppFilterBar.vue'
import AppRow from '../components/AppRow.vue'
import NewAppDialog from '../components/NewAppDialog.vue'
import CatalogPanel from '../components/CatalogPanel.vue'
import SharedAppsSection from '../components/SharedAppsSection.vue'
import { adminAppsService } from '@/features/admin/services/adminAppsService'
import { useAuthStore } from '@/features/auth/stores/auth.store'
import { organizationsService, type OrganizationAppSummary } from '@/features/organizations/services/organizationsService'
import { useAppsStore } from '../stores/apps.store'
import { APP_START_PROGRESS } from '../utils/app-action-error'
import {
  appDescription, appState, matchesFilter, matchesSearch, type AppFilter,
} from '../utils/app-list-display'

const router = useRouter()
const route = useRoute()
const appsStore = useAppsStore()

// Notice for system administrators when permission requests are waiting.
const authStore = useAuthStore()
const waitingRequests = ref(0)
onMounted(async () => {
  if (authStore.user?.roles?.includes('SysAdmin')) waitingRequests.value = await adminAppsService.waitingCount()
})

// Tab state: the Apps list or the Catalog.
const activeTabIndex = ref(route.meta.appsTab === 'catalog' ? 1 : 0)

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
const showNewApp = ref(false)
const newApp = () => { showNewApp.value = true }

const toast = useToast()
const pausing = ref<string | null>(null)

/** Pauses or resumes one app; the row shows the new state when the server has answered. */
async function togglePaused(id: string, name: string, paused: boolean) {
  pausing.value = id
  if (!paused) toast.add({ severity: 'info', summary: 'Resuming', detail: APP_START_PROGRESS, life: 6000 })
  try {
    const warnings = await appsStore.setAppPaused(id, paused)
    if (warnings.length > 0) {
      toast.add({ severity: 'warn', summary: paused ? 'Paused with warnings' : 'Resumed with warnings', detail: warnings.join(' '), life: 8000 })
      return
    }
    toast.add({ severity: 'success', summary: paused ? 'Paused' : 'Resumed', detail: `"${name}" is ${paused ? 'paused' : 'running again'}`, life: 3000 })
  } catch (err: unknown) {
    toast.add({ severity: 'error', summary: 'Error', detail: err instanceof Error ? err.message : 'Could not change the app', life: 5000 })
  } finally {
    pausing.value = null
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

      <p v-if="waitingRequests > 0" class="apps-waiting" data-testid="waiting-notice">
        <RouterLink to="/admin/apps?waiting=true">
          {{ waitingRequests }} permission {{ waitingRequests === 1 ? 'request is' : 'requests are' }} waiting
        </RouterLink>
      </p>

      <AppFilterBar
        v-if="appsStore.hasApps"
        v-model:search="searchQuery"
        label="Search apps"
        group-label="Filter apps"
        placeholder="Search apps"
        :pills="filterPills"
        :active="(value) => filter === value"
        @select="filter = $event as AppFilter"
      />

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
            :state="appState(app)"
          >
            <template #action>
              <Button
                v-if="app.canUpdate"
                :label="app.isPaused ? 'Resume' : 'Pause'"
                :icon="app.isPaused ? 'pi pi-play' : 'pi pi-pause'"
                size="small"
                severity="secondary"
                outlined
                :loading="pausing === app.id"
                :aria-label="`${app.isPaused ? 'Resume' : 'Pause'} ${app.name}`"
                @click="togglePaused(app.id, app.name, !app.isPaused)"
              />
            </template>
          </AppRow>
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

    <NewAppDialog v-model:visible="showNewApp" @catalog="goToCatalog" />

  </div>
</template>

<style scoped>
.apps-view { max-width: 1400px; margin: 0 auto; }
.apps-head { display: flex; flex-wrap: wrap; align-items: flex-start; justify-content: space-between; gap: 16px; margin-bottom: 20px; }
.apps-head__title { margin: 0; font-family: var(--display); font-size: 28px; font-weight: 700; color: var(--ink); }
.apps-head__line { margin: 4px 0 0; color: var(--ink-soft); font-size: 15px; max-width: 52ch; }
.apps-waiting { margin: 0 0 12px; padding: 10px 14px; border: 1px solid var(--signal); border-radius: var(--radius-card); font-size: 14px; }
.apps-waiting a { color: var(--signal-ink); font-weight: 600; }
.apps-head__buttons { display: flex; flex-wrap: wrap; gap: 8px; }
.btn-ink.p-button { min-height: 44px; background: var(--ink); border-color: var(--ink); color: var(--paper); border-radius: 999px; font-weight: 600; }
.btn-ink.p-button:hover { background: var(--ink); border-color: var(--signal); color: var(--paper); }
.btn-outline.p-button { min-height: 44px; background: transparent; border: 1px solid var(--line); color: var(--ink); border-radius: 999px; font-weight: 600; }
.apps-card {
  display: flex; flex-direction: column; gap: 8px; padding: 16px;
  background: var(--paper); border: 1px solid var(--line); border-radius: 18px;
}
.apps-empty { align-items: center; text-align: center; padding: 40px 16px; }
.apps-empty__title { margin: 0; font-family: var(--display); font-size: 20px; font-weight: 600; }
.apps-empty__line { margin: 0 0 8px; color: var(--ink-soft); }
</style>
