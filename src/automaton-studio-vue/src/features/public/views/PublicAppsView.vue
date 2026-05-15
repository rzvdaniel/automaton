<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { useRoute } from 'vue-router'
import Button from 'primevue/button'
import Drawer from 'primevue/drawer'
import Tag from 'primevue/tag'
import ProgressSpinner from 'primevue/progressspinner'
import PublicUserHeader from '../components/PublicUserHeader.vue'
import PublicAppCard from '../components/PublicAppCard.vue'
import AppCoverPlaceholder from '@/features/apps/components/AppCoverPlaceholder.vue'
import { publicAppsService } from '../services/publicApps.service'
import { useAuthStore } from '@/features/auth/stores/auth.store'
import type {
  PublicUserProfile,
  PublicAppCard as AppCardType,
  PublicAppDetails,
} from '../types/publicApps.types'
import { formatPublishedDate, parseTags } from '../types/publicApps.types'
import { useToast } from 'primevue/usetoast'

const route = useRoute()
const authStore = useAuthStore()
const toast = useToast()

const loading = ref(true)
const error = ref<string | null>(null)
const profile = ref<PublicUserProfile | null>(null)
const apps = ref<AppCardType[]>([])
const publicUsername = computed(() => (route.params.publicUsername as string) || '')

const showDrawer = ref(false)
const selectedApp = ref<PublicAppDetails | null>(null)
const loadingDetails = ref(false)
const activeScreenshotUrl = ref('')

function resolveAssetUrl(url?: string | null): string {
  if (!url) return ''
  if (url.startsWith('http')) return url
  return `${import.meta.env.VITE_API_BASE_URL || ''}${url}`
}

const fullThumbnailUrl = computed(() => {
  return resolveAssetUrl(selectedApp.value?.thumbnailUrl)
})

const screenshotGallery = computed(() => selectedApp.value?.screenshots ?? [])

const activeGalleryImageUrl = computed(() => {
  if (activeScreenshotUrl.value) return resolveAssetUrl(activeScreenshotUrl.value)
  if (selectedApp.value?.thumbnailUrl) return fullThumbnailUrl.value
  const firstScreenshot = screenshotGallery.value[0]
  return firstScreenshot ? resolveAssetUrl(firstScreenshot.imageUrl) : ''
})

async function loadUserApps() {
  if (!publicUsername.value) {
    error.value = 'Invalid public username'
    loading.value = false
    return
  }
  loading.value = true
  error.value = null
  try {
    const response = await publicAppsService.getUserApps(publicUsername.value)
    profile.value = response.profile
    apps.value = response.apps
  } catch (err: unknown) {
    if ((err as { response?: { status?: number } })?.response?.status === 404) {
      error.value = 'User not found'
    } else {
      error.value = 'Failed to load apps. Please try again later.'
    }
  } finally {
    loading.value = false
  }
}

function openApp(app: AppCardType) {
  window.open(app.appUrl, '_blank')
}

function showDetails(app: AppCardType) {
  showDrawer.value = true
  loadingDetails.value = true
  selectedApp.value = null
  activeScreenshotUrl.value = ''

  void publicAppsService.getAppDetails(publicUsername.value, app.id)
    .then((details) => {
      selectedApp.value = details
      activeScreenshotUrl.value = details.thumbnailUrl || details.screenshots[0]?.imageUrl || ''
    })
    .catch(() => {
      toast.add({
        severity: 'error',
        summary: 'Details unavailable',
        detail: 'Could not load this app right now.',
        life: 3000,
      })
      showDrawer.value = false
    })
    .finally(() => {
      loadingDetails.value = false
    })
}

async function toggleStar(app: AppCardType) {
  if (!authStore.isAuthenticated) {
    toast.add({
      severity: 'info',
      summary: 'Sign in required',
      detail: 'Sign in to star apps you like.',
      life: 3000,
    })
    return
  }

  const nextStarred = !app.isStarredByCurrentUser
  const nextCount = Math.max(0, app.starCount + (nextStarred ? 1 : -1))

  app.isStarredByCurrentUser = nextStarred
  app.starCount = nextCount

  if (selectedApp.value?.id === app.id) {
    selectedApp.value = { ...selectedApp.value, isStarredByCurrentUser: nextStarred, starCount: nextCount }
  }

  try {
    if (nextStarred) {
      await publicAppsService.starApp(app.id)
    } else {
      await publicAppsService.unstarApp(app.id)
    }
  } catch {
    app.isStarredByCurrentUser = !nextStarred
    app.starCount = Math.max(0, app.starCount + (nextStarred ? -1 : 1))

    if (selectedApp.value?.id === app.id) {
      selectedApp.value = {
        ...selectedApp.value,
        isStarredByCurrentUser: !nextStarred,
        starCount: Math.max(0, (selectedApp.value.starCount ?? 0) + (nextStarred ? -1 : 1)),
      }
    }

    toast.add({
      severity: 'error',
      summary: 'Star failed',
      detail: 'Could not update your star right now.',
      life: 3000,
    })
  }
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
              @details="showDetails"
              @toggle-star="toggleStar"
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

    <!-- App Details Drawer -->
    <Drawer
      v-model:visible="showDrawer"
      position="right"
      :style="{ width: '420px' }"
      :pt="{ header: { style: 'display: none' } }"
    >
      <div v-if="selectedApp" class="drawer-body">
      <div v-if="loadingDetails" class="drawer-loading">
        <ProgressSpinner />
      </div>
      <div v-else-if="selectedApp" class="drawer-body">
        <!-- Cover image -->
        <div class="drawer-cover">
          <img
            v-if="activeGalleryImageUrl"
            :src="activeGalleryImageUrl"
            :alt="selectedApp.name"
          />
          <AppCoverPlaceholder v-else :app-id="selectedApp.id" :name="selectedApp.name" />
          <button class="drawer-close" @click="showDrawer = false">
            <i class="pi pi-times"></i>
          </button>
        </div>

        <!-- Content -->
        <div class="drawer-content">
          <h2 class="drawer-title">{{ selectedApp.name }}</h2>

          <div class="drawer-meta">
            <span v-if="selectedApp.category" class="meta-pill meta-primary">
              {{ selectedApp.category }}
            </span>
            <span v-if="selectedApp.viewCount > 0" class="meta-stat">
              <i class="pi pi-eye"></i> {{ selectedApp.viewCount }}
            </span>
            <span class="meta-stat">
              <i :class="selectedApp.isStarredByCurrentUser ? 'pi pi-star-fill' : 'pi pi-star'"></i> {{ selectedApp.starCount }}
            </span>
            <span v-if="selectedApp.publishedAt" class="meta-stat">
              <i class="pi pi-calendar"></i> {{ formatPublishedDate(selectedApp.publishedAt) }}
            </span>
          </div>

          <p v-if="selectedApp.description || selectedApp.summary" class="drawer-desc">
            {{ selectedApp.description || selectedApp.summary }}
          </p>

          <div v-if="screenshotGallery.length > 0" class="drawer-gallery">
            <button
              v-for="screenshot in screenshotGallery"
              :key="screenshot.id"
              class="gallery-thumb"
              :class="{ 'gallery-thumb-active': activeScreenshotUrl === screenshot.imageUrl }"
              @click="activeScreenshotUrl = screenshot.imageUrl"
            >
              <img
                :src="resolveAssetUrl(screenshot.imageUrl)"
                :alt="`${selectedApp.name} screenshot`"
              />
            </button>
          </div>

          <div v-if="selectedApp.tags" class="drawer-tags">
            <Tag
              v-for="tag in parseTags(selectedApp.tags)"
              :key="tag"
              :value="tag"
              severity="secondary"
            />
          </div>

          <Button
            v-if="authStore.isAuthenticated"
            :label="selectedApp.isStarredByCurrentUser ? 'Unstar' : 'Star'"
            :icon="selectedApp.isStarredByCurrentUser ? 'pi pi-star-fill' : 'pi pi-star'"
            severity="secondary"
            outlined
            class="w-full mb-2"
            @click="toggleStar(selectedApp)"
          />

          <Button
            label="Open App"
            icon="pi pi-arrow-up-right"
            icon-pos="right"
            class="w-full drawer-cta"
            @click="openApp(selectedApp)"
          />
        </div>
      </div>
    </Drawer>
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

@media (max-width: 768px) {
  .apps-grid {
    grid-template-columns: repeat(auto-fill, minmax(200px, 1fr));
  }
}

@media (max-width: 600px) {
  .apps-grid {
    grid-template-columns: repeat(auto-fill, minmax(160px, 1fr));
    gap: 1rem 0.75rem;
  }
}

/* Drawer */
.drawer-body {
  display: flex;
  flex-direction: column;
  height: 100%;
  overflow-y: auto;
}

.drawer-loading {
  min-height: 18rem;
  display: flex;
  align-items: center;
  justify-content: center;
}

/* Cover — 16:9 within drawer width */
.drawer-cover {
  position: relative;
  width: 100%;
  padding-bottom: 56.25%;
  overflow: hidden;
  flex-shrink: 0;
}

.drawer-cover img {
  position: absolute;
  inset: 0;
  width: 100%;
  height: 100%;
  object-fit: cover;
}

.drawer-close {
  position: absolute;
  top: 0.75rem;
  right: 0.75rem;
  width: 2rem;
  height: 2rem;
  border-radius: 50%;
  border: none;
  background: rgba(0, 0, 0, 0.55);
  color: white;
  font-size: 0.9rem;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  transition: background 0.2s ease;
  z-index: 10;
}

.drawer-close:hover {
  background: rgba(0, 0, 0, 0.8);
}

/* Content */
.drawer-content {
  padding: 1.5rem;
  display: flex;
  flex-direction: column;
  gap: 1rem;
  flex: 1;
}

.drawer-title {
  margin: 0;
  font-size: 1.5rem;
  font-weight: 700;
  line-height: 1.25;
  color: var(--p-text-color);
}

.drawer-meta {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 0.6rem;
}

.meta-pill {
  padding: 0.2rem 0.65rem;
  border-radius: 4px;
  font-size: 0.8rem;
  font-weight: 600;
}

.meta-primary {
  background: var(--p-primary-500);
  color: white;
}

.meta-stat {
  display: flex;
  align-items: center;
  gap: 0.3rem;
  font-size: 0.85rem;
  color: var(--p-text-muted-color);
}

.meta-stat i {
  font-size: 0.75rem;
}

.drawer-desc {
  margin: 0;
  font-size: 0.95rem;
  line-height: 1.65;
  color: var(--p-text-color);
}

.drawer-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 0.4rem;
}

.drawer-gallery {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(78px, 1fr));
  gap: 0.55rem;
}

.gallery-thumb {
  padding: 0;
  border: 2px solid transparent;
  border-radius: 10px;
  overflow: hidden;
  background: var(--p-surface-100);
  cursor: pointer;
  aspect-ratio: 16 / 9;
}

.gallery-thumb img {
  width: 100%;
  height: 100%;
  object-fit: cover;
  display: block;
}

.gallery-thumb-active {
  border-color: var(--p-primary-500);
}

.drawer-cta {
  margin-top: auto;
  font-weight: 600;
}
</style>
