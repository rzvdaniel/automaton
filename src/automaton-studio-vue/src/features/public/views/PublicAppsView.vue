<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { useRoute } from 'vue-router'
import Button from 'primevue/button'
import Dialog from 'primevue/dialog'
import Tag from 'primevue/tag'
import ProgressSpinner from 'primevue/progressspinner'
import AppToolbar from '@/shared/components/layout/AppToolbar.vue'
import AppSidebar from '@/shared/components/layout/AppSidebar.vue'
import PublicUserHeader from '../components/PublicUserHeader.vue'
import PublicAppCard from '../components/PublicAppCard.vue'
import { publicAppsService } from '../services/publicApps.service'
import { useAuthStore } from '@/features/auth/stores/auth.store'
import type { PublicUserProfile, PublicAppCard as AppCardType } from '../types/publicApps.types'
import { formatPublishedDate, parseTags } from '../types/publicApps.types'

const route = useRoute()
const authStore = useAuthStore()

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
  const publicUsername = route.params.publicUsername as string

  if (!publicUsername) {
    error.value = 'Invalid public username'
    loading.value = false
    return
  }

  loading.value = true
  error.value = null

  try {
    const response = await publicAppsService.getUserApps(publicUsername)
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
    <!-- Public Header (Hidden when authenticated) -->
    <header v-if="!authStore.isAuthenticated" class="public-header">
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

    <!-- App Details Dialog - Netflix Style -->
    <Dialog
      v-model:visible="showDetailsDialog"
      modal
      :style="{ width: '90vw', maxWidth: '900px' }"
      :closable="true"
      :header="null"
      class="netflix-dialog"
    >
      <template #container="{ closeCallback }">
        <div class="netflix-modal-content">
          <button class="modal-close-btn" @click="closeCallback">
            <i class="pi pi-times"></i>
          </button>

          <div v-if="selectedApp" class="modal-body">
            <!-- Thumbnail -->
            <div v-if="selectedApp.thumbnailUrl" class="modal-thumbnail">
              <img :src="fullThumbnailUrl" :alt="selectedApp.name" />
            </div>

            <!-- Content -->
            <div class="modal-content">
              <!-- Title & Actions -->
              <div class="modal-header">
                <div>
                  <h2 class="modal-title">{{ selectedApp.name }}</h2>
                  <div class="modal-meta">
                    <span v-if="selectedApp.category" class="meta-category">
                      {{ selectedApp.category }}
                    </span>
                    <span class="meta-views">
                      <i class="pi pi-eye"></i>
                      {{ selectedApp.viewCount }} views
                    </span>
                    <span v-if="selectedApp.publishedAt" class="meta-date">
                      <i class="pi pi-calendar"></i>
                      {{ formatPublishedDate(selectedApp.publishedAt) }}
                    </span>
                  </div>
                </div>
                <Button
                  icon="pi pi-play"
                  class="modal-play-btn"
                  @click="selectedApp && openApp(selectedApp)"
                />
              </div>

              <!-- Description -->
              <div v-if="selectedApp.description" class="modal-description">
                <p>{{ selectedApp.description }}</p>
              </div>

              <!-- Tags -->
              <div v-if="(selectedApp as any).tags" class="modal-tags">
                <Tag
                  v-for="tag in parseTags((selectedApp as any).tags)"
                  :key="tag"
                  :value="tag"
                  severity="secondary"
                />
              </div>
            </div>
          </div>
        </div>
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
  max-width: 1400px;
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

/* Apps Grid - Netflix Style */
.apps-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
  gap: 1.5rem 1rem;
  padding-bottom: 1rem;
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

/* Netflix Modal Dialog */
:global(.netflix-dialog .p-dialog) {
  border-radius: 8px;
}

:global(.netflix-dialog .p-dialog-mask) {
  background-color: rgba(0, 0, 0, 0.7);
  backdrop-filter: blur(4px);
}

.netflix-modal-content {
  position: relative;
  background: var(--p-surface-card);
  border-radius: 8px;
  overflow: hidden;
  max-height: 85vh;
  overflow-y: auto;
}

:global(.app-dark) .netflix-modal-content {
  background: var(--p-surface-800);
}

.modal-close-btn {
  position: absolute;
  top: 1rem;
  right: 1rem;
  width: 2.5rem;
  height: 2.5rem;
  border-radius: 50%;
  border: none;
  background: rgba(0, 0, 0, 0.5);
  color: white;
  font-size: 1.2rem;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  transition: all 0.2s ease;
  z-index: 10;
}

.modal-close-btn:hover {
  background: rgba(0, 0, 0, 0.8);
  transform: scale(1.1);
}

.modal-body {
  display: flex;
  flex-direction: column;
  max-height: 85vh;
}

.modal-thumbnail {
  width: 100%;
  height: 300px;
  overflow: hidden;
  background: var(--p-surface-700);
}

.modal-thumbnail img {
  width: 100%;
  height: 100%;
  object-fit: cover;
}

.modal-content {
  padding: 2rem;
  flex: 1;
  overflow-y: auto;
}

.modal-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 2rem;
  margin-bottom: 1.5rem;
}

.modal-title {
  margin: 0 0 0.5rem 0;
  font-size: 2rem;
  font-weight: 700;
  color: var(--p-text-color);
  line-height: 1.2;
}

.modal-meta {
  display: flex;
  align-items: center;
  gap: 1.5rem;
  font-size: 0.9rem;
  color: var(--p-text-muted-color);
  margin-top: 0.5rem;
  flex-wrap: wrap;
}

.meta-category {
  background: var(--p-primary-500);
  color: white;
  padding: 0.25rem 0.75rem;
  border-radius: 4px;
  font-size: 0.85rem;
  font-weight: 500;
}

.meta-views,
.meta-date {
  display: flex;
  align-items: center;
  gap: 0.4rem;
}

.modal-play-btn {
  padding: 0.75rem 1.5rem;
  font-weight: 600;
  align-self: flex-start;
}

.modal-description {
  margin-bottom: 1.5rem;
  line-height: 1.6;
  color: var(--p-text-color);
}

.modal-description p {
  margin: 0;
}

.modal-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
}

@media (max-width: 768px) {
  .apps-grid {
    grid-template-columns: repeat(auto-fill, minmax(200px, 1fr));
  }

  :global(.netflix-dialog .p-dialog) {
    width: 95vw !important;
  }

  .modal-header {
    flex-direction: column;
  }

  .modal-thumbnail {
    height: 200px;
  }

  .modal-content {
    padding: 1.5rem;
  }

  .modal-title {
    font-size: 1.5rem;
  }
}

@media (max-width: 600px) {
  .apps-grid {
    grid-template-columns: repeat(auto-fill, minmax(160px, 1fr));
    gap: 1rem 0.75rem;
  }

  .modal-meta {
    gap: 0.75rem;
    font-size: 0.8rem;
  }
}
</style>
