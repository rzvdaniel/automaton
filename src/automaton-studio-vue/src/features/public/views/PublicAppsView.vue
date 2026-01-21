<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { useRoute } from 'vue-router'
import Button from 'primevue/button'
import Dialog from 'primevue/dialog'
import Tag from 'primevue/tag'
import ProgressSpinner from 'primevue/progressspinner'
import PublicUserHeader from '../components/PublicUserHeader.vue'
import PublicAppCard from '../components/PublicAppCard.vue'
import { publicAppsService } from '../services/publicApps.service'
import type { PublicUserProfile, PublicAppCard as AppCardType } from '../types/publicApps.types'
import { formatPublishedDate, parseTags } from '../types/publicApps.types'

const route = useRoute()

// State
const loading = ref(true)
const error = ref<string | null>(null)
const profile = ref<PublicUserProfile | null>(null)
const apps = ref<AppCardType[]>([])

// Details dialog
const showDetailsDialog = ref(false)
const selectedApp = ref<AppCardType | null>(null)

// Full thumbnail URL with API base URL
const fullThumbnailUrl = computed(() => {
  if (!selectedApp.value?.thumbnailUrl) return ''
  // If already a full URL, use as-is; otherwise prepend API base URL
  if (selectedApp.value.thumbnailUrl.startsWith('http')) {
    return selectedApp.value.thumbnailUrl
  }
  const baseUrl = import.meta.env.VITE_API_BASE_URL || ''
  return `${baseUrl}${selectedApp.value.thumbnailUrl}`
})

// Load data
async function loadUserApps() {
  const username = route.params.username as string

  if (!username) {
    error.value = 'Invalid username'
    loading.value = false
    return
  }

  loading.value = true
  error.value = null

  try {
    const response = await publicAppsService.getUserApps(username)
    profile.value = response.profile
    apps.value = response.apps
  } catch (err: unknown) {
    console.error('Failed to load user apps:', err)
    if ((err as { response?: { status?: number } })?.response?.status === 404) {
      error.value = 'User not found'
    } else {
      error.value = 'Failed to load apps. Please try again later.'
    }
  } finally {
    loading.value = false
  }
}

// Open app in new tab
function openApp(app: AppCardType) {
  window.open(app.appUrl, '_blank')
}

// Show app details
function showDetails(app: AppCardType) {
  selectedApp.value = app
  showDetailsDialog.value = true
}

onMounted(() => {
  loadUserApps()
})
</script>

<template>
  <div class="public-apps-view">
    <!-- Public Header (Minimal) -->
    <header class="public-header">
      <div class="header-inner">
        <router-link to="/" class="logo">
          <span class="logo-text">Automaton</span>
        </router-link>
        <div class="header-actions">
          <router-link to="/login">
            <Button label="Sign In" text size="small" />
          </router-link>
          <router-link to="/register">
            <Button label="Get Started" size="small" />
          </router-link>
        </div>
      </div>
    </header>

    <!-- Main Content -->
    <main class="public-main">
      <!-- Loading State -->
      <div v-if="loading" class="loading-state">
        <ProgressSpinner />
        <p>Loading...</p>
      </div>

      <!-- Error State -->
      <div v-else-if="error" class="error-state">
        <i class="pi pi-exclamation-triangle"></i>
        <h2>{{ error }}</h2>
        <p v-if="error === 'User not found'">
          This user doesn't exist or hasn't published any apps yet.
        </p>
        <router-link to="/">
          <Button label="Go to Homepage" />
        </router-link>
      </div>

      <!-- User Profile & Apps -->
      <template v-else-if="profile">
        <PublicUserHeader :profile="profile" />

        <!-- Apps Grid -->
        <div class="apps-section">
          <div class="section-header">
            <h2>Published Apps</h2>
          </div>

          <!-- Empty State -->
          <div v-if="apps.length === 0" class="empty-state">
            <i class="pi pi-inbox"></i>
            <h3>No public apps yet</h3>
            <p>This user hasn't published any apps yet. Check back later!</p>
          </div>

          <!-- Apps Grid -->
          <div v-else class="apps-grid">
            <PublicAppCard
              v-for="app in apps"
              :key="app.id"
              :app="app"
              @open="openApp"
              @details="showDetails"
            />
          </div>
        </div>
      </template>
    </main>

    <!-- Footer -->
    <footer class="public-footer">
      <div class="footer-inner">
        <p>
          Built with <strong>Automaton</strong> -
          <router-link to="/register">Create your own apps</router-link>
        </p>
      </div>
    </footer>

    <!-- App Details Dialog -->
    <Dialog
      v-model:visible="showDetailsDialog"
      :header="selectedApp?.displayName || selectedApp?.name || 'App Details'"
      modal
      :style="{ width: '500px' }"
    >
      <div v-if="selectedApp" class="app-details">
        <!-- Thumbnail -->
        <div v-if="selectedApp.thumbnailUrl" class="details-thumbnail">
          <img :src="fullThumbnailUrl" :alt="selectedApp.name" />
        </div>

        <!-- Description -->
        <div class="details-section">
          <h4>Description</h4>
          <p v-if="selectedApp.description">{{ selectedApp.description }}</p>
          <p v-else class="text-muted">No description provided</p>
        </div>

        <!-- Category & Tags -->
        <div v-if="selectedApp.category || (selectedApp as any).tags" class="details-section">
          <h4>Category & Tags</h4>
          <div class="tags-row">
            <Tag v-if="selectedApp.category" :value="selectedApp.category" severity="info" />
            <Tag
              v-for="tag in parseTags((selectedApp as any).tags)"
              :key="tag"
              :value="tag"
              severity="secondary"
            />
          </div>
        </div>

        <!-- Stats -->
        <div class="details-section">
          <h4>Statistics</h4>
          <div class="stats-row">
            <div class="stat-item">
              <i class="pi pi-eye"></i>
              <span>{{ selectedApp.viewCount }} views</span>
            </div>
            <div v-if="selectedApp.publishedAt" class="stat-item">
              <i class="pi pi-calendar"></i>
              <span>Published {{ formatPublishedDate(selectedApp.publishedAt) }}</span>
            </div>
          </div>
        </div>
      </div>

      <template #footer>
        <Button label="Close" text @click="showDetailsDialog = false" />
        <Button
          label="Open App"
          icon="pi pi-external-link"
          @click="selectedApp && openApp(selectedApp)"
        />
      </template>
    </Dialog>
  </div>
</template>

<style scoped>
.public-apps-view {
  min-height: 100vh;
  display: flex;
  flex-direction: column;
  background: var(--p-surface-ground);
}

/* Header */
.public-header {
  border-bottom: 1px solid var(--p-surface-200);
  background: var(--p-surface-card);
  position: sticky;
  top: 0;
  z-index: 100;
}

:global(.app-dark) .public-header {
  border-color: var(--p-surface-700);
}

.header-inner {
  max-width: 1200px;
  margin: 0 auto;
  padding: 0.75rem 1.5rem;
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.logo {
  text-decoration: none;
}

.logo-text {
  font-size: 1.5rem;
  font-weight: 700;
  color: var(--p-primary-500);
}

.header-actions {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

/* Main */
.public-main {
  flex: 1;
  padding-bottom: 2rem;
}

/* Loading State */
.loading-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 4rem 2rem;
  color: var(--p-text-muted-color);
}

.loading-state p {
  margin-top: 1rem;
}

/* Error State */
.error-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 4rem 2rem;
  text-align: center;
}

.error-state i {
  font-size: 4rem;
  color: var(--p-orange-500);
  margin-bottom: 1rem;
}

.error-state h2 {
  margin: 0 0 0.5rem 0;
  color: var(--p-text-color);
}

.error-state p {
  margin: 0 0 1.5rem 0;
  color: var(--p-text-muted-color);
}

/* Apps Section */
.apps-section {
  max-width: 1200px;
  margin: 0 auto;
  padding: 0 1.5rem;
}

.section-header {
  margin-bottom: 1.5rem;
}

.section-header h2 {
  margin: 0;
  font-size: 1.5rem;
  font-weight: 600;
  color: var(--p-text-color);
}

/* Empty State */
.empty-state {
  text-align: center;
  padding: 4rem 2rem;
  color: var(--p-text-muted-color);
}

.empty-state i {
  font-size: 4rem;
  margin-bottom: 1rem;
  opacity: 0.5;
}

.empty-state h3 {
  margin: 0 0 0.5rem 0;
  color: var(--p-text-color);
}

.empty-state p {
  margin: 0;
}

/* Apps Grid */
.apps-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
  gap: 1.5rem;
}

/* Footer */
.public-footer {
  border-top: 1px solid var(--p-surface-200);
  background: var(--p-surface-card);
  padding: 1.5rem;
  margin-top: auto;
}

:global(.app-dark) .public-footer {
  border-color: var(--p-surface-700);
}

.footer-inner {
  max-width: 1200px;
  margin: 0 auto;
  text-align: center;
}

.footer-inner p {
  margin: 0;
  color: var(--p-text-muted-color);
  font-size: 0.9rem;
}

.footer-inner a {
  color: var(--p-primary-500);
  text-decoration: none;
}

.footer-inner a:hover {
  text-decoration: underline;
}

/* App Details Dialog */
.app-details {
  display: flex;
  flex-direction: column;
  gap: 1.25rem;
}

.details-thumbnail {
  border-radius: 8px;
  overflow: hidden;
}

.details-thumbnail img {
  width: 100%;
  height: 200px;
  object-fit: cover;
}

.details-section h4 {
  margin: 0 0 0.5rem 0;
  font-size: 0.9rem;
  font-weight: 600;
  color: var(--p-text-muted-color);
  text-transform: uppercase;
  letter-spacing: 0.5px;
}

.details-section p {
  margin: 0;
  color: var(--p-text-color);
  line-height: 1.5;
}

.text-muted {
  color: var(--p-text-muted-color);
  font-style: italic;
}

.tags-row {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
}

.stats-row {
  display: flex;
  flex-wrap: wrap;
  gap: 1.5rem;
}

.stat-item {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  color: var(--p-text-muted-color);
  font-size: 0.9rem;
}

.stat-item i {
  color: var(--p-primary-500);
}

@media (max-width: 600px) {
  .apps-grid {
    grid-template-columns: 1fr;
  }
}
</style>
