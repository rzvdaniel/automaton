<script setup lang="ts">
import { onMounted, onUnmounted, computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import Card from 'primevue/card'
import Button from 'primevue/button'
import Tag from 'primevue/tag'
import Skeleton from 'primevue/skeleton'
import Message from 'primevue/message'
import { useToast } from 'primevue/usetoast'
import { useDashboardStore } from '../stores/dashboard.store'
import WelcomeSection from './WelcomeSection.vue'
import { getUserAiApiKeys } from '@/shared/services/ai/ai-providers-service'

const router = useRouter()
const toast = useToast()
const dashboardStore = useDashboardStore()

// Track whether the user has configured any AI keys
const hasAiKeys = ref(false)

// Fetch dashboard data on mount
onMounted(async () => {
  await dashboardStore.fetchDashboard()
  // Check AI keys in parallel with auto-refresh (lightweight)
  try {
    const keys = await getUserAiApiKeys()
    hasAiKeys.value = keys.length > 0
  } catch {
    // Non-critical — checklist just shows step as incomplete
  }
  // Start auto-refresh every 30 seconds
  dashboardStore.startAutoRefresh(30000)
})

// Stop auto-refresh on unmount
onUnmounted(() => {
  dashboardStore.stopAutoRefresh()
})

// Navigation handlers
const handleCreateApp = () => {
  router.push('/apps')
}

const handleViewAllApps = () => {
  router.push('/apps')
}

const handleAppClick = (appId: string) => {
  router.push(`/apps/${appId}`)
}

// Format time ago helper
const formatTimeAgo = (dateString: string): string => {
  const date = new Date(dateString)
  const now = new Date()
  const diffMs = now.getTime() - date.getTime()
  const diffMins = Math.floor(diffMs / 60000)
  const diffHours = Math.floor(diffMs / 3600000)
  const diffDays = Math.floor(diffMs / 86400000)

  if (diffMins < 1) return 'Just now'
  if (diffMins < 60) return `${diffMins}m ago`
  if (diffHours < 24) return `${diffHours}h ago`
  return `${diffDays}d ago`
}

// Copy URL to clipboard
const copyPublicUrl = async (url: string) => {
  try {
    const fullUrl = `${window.location.origin}/web/${url}`
    await navigator.clipboard.writeText(fullUrl)
    toast.add({
      severity: 'success',
      summary: 'Copied',
      detail: 'URL copied to clipboard',
      life: 2000,
    })
  } catch {
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: 'Failed to copy URL',
      life: 3000,
    })
  }
}

// Open published app in new tab
const openPublishedApp = (url: string) => {
  window.open(`/web/${url}`, '_blank')
}

// Apps display text
const appsCountText = computed(() => {
  const total = dashboardStore.totalApps
  if (total === 0) return 'No apps yet'
  return `${total} total`
})

// Retry fetch on error
const handleRetry = () => {
  dashboardStore.clearError()
  dashboardStore.fetchDashboard()
}

// Dismiss welcome section
const handleDismissWelcome = () => {
  dashboardStore.dismissWelcome()
}

// Show checklist when: not all 4 setup steps done AND user hasn't dismissed it
const allSetupDone = computed(
  () =>
    hasAiKeys.value &&
    dashboardStore.runnersOnline > 0 &&
    dashboardStore.totalApps > 0 &&
    dashboardStore.publishedApps > 0,
)
const showChecklist = computed(() => !allSetupDone.value && !dashboardStore.isWelcomeDismissed)
</script>

<template>
  <div class="standard-dashboard">
    <!-- Page Header -->
    <div class="flex align-items-center justify-content-between mb-4">
      <div>
        <h1 class="text-3xl font-bold m-0">Dashboard</h1>
        <p class="text-color-secondary mt-1 mb-0">Build and manage your web applications</p>
      </div>
      <Button label="Create App" icon="pi pi-plus" @click="handleCreateApp" />
    </div>

    <!-- Error State -->
    <Message v-if="dashboardStore.error" severity="error" :closable="false" class="mb-4">
      <div class="flex align-items-center justify-content-between w-full">
        <span>{{ dashboardStore.error }}</span>
        <Button label="Retry" icon="pi pi-refresh" size="small" text @click="handleRetry" />
      </div>
    </Message>

    <!-- Setup Checklist (hidden once all 4 steps done or manually dismissed) -->
    <WelcomeSection
      v-if="showChecklist && !dashboardStore.isLoading"
      :has-ai-keys="hasAiKeys"
      :has-runners="dashboardStore.runnersOnline > 0"
      :has-apps="dashboardStore.totalApps > 0"
      :has-published-app="dashboardStore.publishedApps > 0"
      @dismiss="handleDismissWelcome"
    />

    <!-- Stats Cards -->
    <div v-if="dashboardStore.isLoading && !dashboardStore.hasData" class="grid mb-4">
      <div v-for="i in 4" :key="i" class="col-12 md:col-6 lg:col-3">
        <Card class="h-full">
          <template #content>
            <div class="flex align-items-center gap-3">
              <Skeleton shape="circle" size="48px" />
              <div class="flex-1">
                <Skeleton width="60%" height="14px" class="mb-2" />
                <Skeleton width="40%" height="24px" class="mb-2" />
                <Skeleton width="80%" height="12px" />
              </div>
            </div>
          </template>
        </Card>
      </div>
    </div>

    <div v-else class="grid mb-4">
      <!-- My Apps -->
      <div class="col-12 md:col-6 lg:col-3">
        <Card class="h-full stat-card" @click="handleViewAllApps">
          <template #content>
            <div class="flex align-items-center gap-3">
              <div class="stat-icon stat-icon-primary">
                <i class="pi pi-globe text-xl"></i>
              </div>
              <div>
                <div class="text-color-secondary text-sm">My Apps</div>
                <div class="text-2xl font-bold">{{ dashboardStore.totalApps }}</div>
                <div class="text-color-secondary text-xs">{{ appsCountText }}</div>
              </div>
            </div>
          </template>
        </Card>
      </div>

      <!-- Published Apps -->
      <div class="col-12 md:col-6 lg:col-3">
        <Card class="h-full stat-card">
          <template #content>
            <div class="flex align-items-center gap-3">
              <div class="stat-icon stat-icon-green">
                <i class="pi pi-check-circle text-xl"></i>
              </div>
              <div>
                <div class="text-color-secondary text-sm">Published</div>
                <div class="text-2xl font-bold">{{ dashboardStore.publishedApps }}</div>
                <div class="text-color-secondary text-xs">Live apps</div>
              </div>
            </div>
          </template>
        </Card>
      </div>

      <!-- Total Views -->
      <div class="col-12 md:col-6 lg:col-3">
        <Card class="h-full stat-card">
          <template #content>
            <div class="flex align-items-center gap-3">
              <div class="stat-icon stat-icon-teal">
                <i class="pi pi-eye text-xl"></i>
              </div>
              <div>
                <div class="text-color-secondary text-sm">Total Views</div>
                <div class="text-2xl font-bold">{{ dashboardStore.totalAppViews }}</div>
                <div class="text-color-secondary text-xs">All time</div>
              </div>
            </div>
          </template>
        </Card>
      </div>

      <!-- Runners -->
      <div class="col-12 md:col-6 lg:col-3">
        <Card
          class="h-full stat-card"
          :class="{ 'stat-card-warning': dashboardStore.runnersOnline === 0 && !dashboardStore.isLoading }"
          @click="router.push('/runners')"
        >
          <template #content>
            <div class="flex align-items-center gap-3">
              <div
                class="stat-icon"
                :class="dashboardStore.runnersOnline === 0 && !dashboardStore.isLoading ? 'stat-icon-warning' : 'stat-icon-blue'"
              >
                <i class="pi pi-desktop text-xl"></i>
              </div>
              <div>
                <div class="text-color-secondary text-sm">Runners</div>
                <div class="text-2xl font-bold">
                  {{ dashboardStore.runnersOnline }} / {{ dashboardStore.runnersTotal }}
                </div>
                <div
                  v-if="dashboardStore.runnersOnline === 0 && !dashboardStore.isLoading"
                  class="text-xs"
                  style="color: var(--p-orange-500)"
                >
                  Needed for backend deploy
                </div>
                <div v-else class="text-color-secondary text-xs">Online</div>
              </div>
            </div>
          </template>
        </Card>
      </div>
    </div>

    <!-- Main Content Grid -->
    <div class="grid">
      <!-- Recent Apps -->
      <div class="col-12 lg:col-8">
        <Card>
          <template #title>
            <div class="flex align-items-center justify-content-between">
              <span>My Recent Apps</span>
              <Button label="View All" link size="small" @click="handleViewAllApps" />
            </div>
          </template>
          <template #content>
            <!-- Loading state -->
            <div v-if="dashboardStore.isLoading && !dashboardStore.hasData" class="grid">
              <div v-for="i in 3" :key="i" class="col-12 md:col-6 lg:col-4">
                <Skeleton height="120px" class="border-round" />
              </div>
            </div>

            <!-- Empty state -->
            <div
              v-else-if="dashboardStore.recentApps.length === 0"
              class="flex flex-column align-items-center justify-content-center py-5"
            >
              <i class="pi pi-globe text-color-secondary" style="font-size: 3rem"></i>
              <p class="text-color-secondary mt-3 mb-2">No apps yet</p>
              <p class="text-color-secondary text-sm m-0">Create your first app to get started</p>
              <Button
                label="Create Your First App"
                icon="pi pi-plus"
                class="mt-4"
                @click="handleCreateApp"
              />
            </div>

            <!-- Recent apps grid -->
            <div v-else class="grid">
              <div
                v-for="app in dashboardStore.recentApps"
                :key="app.id"
                class="col-12 md:col-6 lg:col-4"
              >
                <div class="recent-app-card" @click="handleAppClick(app.id)">
                  <div class="recent-app-header">
                    <div class="recent-app-icon">
                      <i class="pi pi-globe"></i>
                    </div>
                    <Tag
                      :value="app.isPublished ? 'Published' : 'Draft'"
                      :severity="app.isPublished ? 'success' : 'secondary'"
                      class="text-xs"
                    />
                  </div>
                  <div class="recent-app-name">{{ app.name }}</div>
                  <div class="recent-app-meta">
                    <span v-if="app.viewCount > 0">
                      <i class="pi pi-eye"></i> {{ app.viewCount }} views
                    </span>
                    <span>Updated {{ formatTimeAgo(app.updated) }}</span>
                  </div>
                </div>
              </div>
            </div>
          </template>
        </Card>
      </div>

      <!-- Published Apps -->
      <div class="col-12 lg:col-4">
        <Card>
          <template #title>Published Apps</template>
          <template #content>
            <!-- Loading state -->
            <div
              v-if="dashboardStore.isLoading && !dashboardStore.hasData"
              class="flex flex-column gap-3"
            >
              <Skeleton height="48px" />
              <Skeleton height="48px" />
              <Skeleton height="48px" />
            </div>

            <!-- Empty state -->
            <div
              v-else-if="dashboardStore.publishedAppsList.length === 0"
              class="flex flex-column align-items-center py-4"
            >
              <i class="pi pi-globe text-color-secondary" style="font-size: 2rem"></i>
              <p class="text-color-secondary mt-2 mb-0 text-sm">No published apps yet</p>
            </div>

            <!-- Published apps list -->
            <div v-else class="flex flex-column gap-2">
              <div
                v-for="app in dashboardStore.publishedAppsList"
                :key="app.id"
                class="published-app-item"
              >
                <div class="published-app-info" @click="openPublishedApp(app.publicUrl)">
                  <span class="published-app-name">{{ app.name }}</span>
                  <span class="published-app-url">/web/{{ app.publicUrl }}</span>
                </div>
                <div class="published-app-actions">
                  <span v-if="app.viewCount > 0" class="published-app-views">
                    {{ app.viewCount }}
                  </span>
                  <Button
                    icon="pi pi-copy"
                    text
                    rounded
                    size="small"
                    severity="secondary"
                    aria-label="Copy URL"
                    @click.stop="copyPublicUrl(app.publicUrl)"
                  />
                  <Button
                    icon="pi pi-external-link"
                    text
                    rounded
                    size="small"
                    severity="secondary"
                    aria-label="Open app"
                    @click.stop="openPublishedApp(app.publicUrl)"
                  />
                </div>
              </div>
            </div>
          </template>
        </Card>
      </div>
    </div>
  </div>
</template>

<style scoped>
.standard-dashboard {
  max-width: 1400px;
  margin: 0 auto;
}

.stat-card {
  transition: transform 0.2s ease, box-shadow 0.2s ease;
  cursor: pointer;
}

.stat-card:hover {
  transform: translateY(-2px);
}

.stat-icon {
  width: 48px;
  height: 48px;
  border-radius: 8px;
  display: flex;
  align-items: center;
  justify-content: center;
}

.stat-icon-primary {
  background-color: var(--p-primary-100);
  color: var(--p-primary-600);
}

.stat-icon-green {
  background-color: var(--p-green-100);
  color: var(--p-green-600);
}

.stat-icon-teal {
  background-color: var(--p-teal-100);
  color: var(--p-teal-600);
}

.stat-icon-blue {
  background-color: var(--p-blue-100);
  color: var(--p-blue-600);
}

.stat-icon-warning {
  background-color: var(--p-orange-100);
  color: var(--p-orange-600);
}

.stat-card-warning {
  border-left: 3px solid var(--p-orange-400);
}

:global(.app-dark) .stat-icon-primary {
  background-color: var(--p-primary-900);
  color: var(--p-primary-400);
}

:global(.app-dark) .stat-icon-green {
  background-color: var(--p-green-900);
  color: var(--p-green-400);
}

:global(.app-dark) .stat-icon-teal {
  background-color: var(--p-teal-900);
  color: var(--p-teal-400);
}

:global(.app-dark) .stat-icon-blue {
  background-color: var(--p-blue-900);
  color: var(--p-blue-400);
}

:global(.app-dark) .stat-icon-warning {
  background-color: var(--p-orange-900);
  color: var(--p-orange-400);
}

/* Recent app cards */
.recent-app-card {
  border: 1px solid var(--p-surface-200);
  border-radius: 8px;
  padding: 1rem;
  cursor: pointer;
  transition: all 0.2s ease;
}

.recent-app-card:hover {
  border-color: var(--p-primary-500);
  transform: translateY(-2px);
}

:global(.app-dark) .recent-app-card {
  border-color: var(--p-surface-700);
}

:global(.app-dark) .recent-app-card:hover {
  border-color: var(--p-primary-500);
}

.recent-app-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 0.75rem;
}

.recent-app-icon {
  width: 36px;
  height: 36px;
  border-radius: 8px;
  background: var(--p-primary-100);
  display: flex;
  align-items: center;
  justify-content: center;
}

:global(.app-dark) .recent-app-icon {
  background: var(--p-primary-900);
}

.recent-app-icon i {
  font-size: 1.125rem;
  color: var(--p-primary-500);
}

.recent-app-name {
  font-weight: 600;
  color: var(--p-text-color);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  margin-bottom: 0.5rem;
}

.recent-app-meta {
  display: flex;
  gap: 1rem;
  font-size: 0.75rem;
  color: var(--p-text-muted-color);
}

.recent-app-meta i {
  font-size: 0.75rem;
  margin-right: 0.25rem;
}

/* Published apps list */
.published-app-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0.5rem;
  border-radius: 6px;
  transition: background-color 0.2s ease;
}

.published-app-item:hover {
  background-color: var(--p-surface-hover);
}

.published-app-info {
  display: flex;
  flex-direction: column;
  cursor: pointer;
  min-width: 0;
  flex: 1;
}

.published-app-name {
  font-weight: 500;
  color: var(--p-text-color);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.published-app-url {
  font-size: 0.75rem;
  color: var(--p-text-muted-color);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.published-app-actions {
  display: flex;
  align-items: center;
  gap: 0.25rem;
  flex-shrink: 0;
}

.published-app-views {
  font-size: 0.75rem;
  color: var(--p-text-muted-color);
  margin-right: 0.25rem;
}
</style>
