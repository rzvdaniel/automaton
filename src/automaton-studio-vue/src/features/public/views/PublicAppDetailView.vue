<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import Button from 'primevue/button'
import Tag from 'primevue/tag'
import ProgressSpinner from 'primevue/progressspinner'
import Avatar from 'primevue/avatar'
import AppCoverPlaceholder from '@/features/apps/components/AppCoverPlaceholder.vue'
import { publicAppsService } from '../services/publicApps.service'
import { useAuthStore } from '@/features/auth/stores/auth.store'
import type { PublicAppDetails, PublicUserProfile } from '../types/publicApps.types'
import { formatPublishedDate, getInitials, parseTags } from '../types/publicApps.types'
import { useToast } from 'primevue/usetoast'

const route = useRoute()
const router = useRouter()
const authStore = useAuthStore()
const toast = useToast()

const loading = ref(true)
const error = ref<string | null>(null)
const profile = ref<PublicUserProfile | null>(null)
const app = ref<PublicAppDetails | null>(null)
const activeImageUrl = ref('')

const publicUsername = computed(() => (route.params.publicUsername as string) || '')
const appId = computed(() => (route.params.appId as string) || '')

function resolveAssetUrl(url?: string | null): string {
  if (!url) return ''
  if (url.startsWith('http')) return url
  return `${import.meta.env.VITE_API_BASE_URL || ''}${url}`
}

const heroImageUrl = computed(() => {
  if (activeImageUrl.value) return resolveAssetUrl(activeImageUrl.value)
  if (app.value?.thumbnailUrl) return resolveAssetUrl(app.value.thumbnailUrl)
  const firstScreenshot = app.value?.screenshots?.[0]
  return firstScreenshot ? resolveAssetUrl(firstScreenshot.imageUrl) : ''
})

const galleryImages = computed(() => {
  if (!app.value) return []

  const items = []

  if (app.value.thumbnailUrl) {
    items.push({
      key: 'thumbnail',
      imageUrl: app.value.thumbnailUrl,
      label: 'Cover',
    })
  }

  for (const screenshot of app.value.screenshots ?? []) {
    items.push({
      key: screenshot.id,
      imageUrl: screenshot.imageUrl,
      label: screenshot.caption || 'Screenshot',
    })
  }

  return items
})

async function loadPage() {
  if (!publicUsername.value || !appId.value) {
    error.value = 'Invalid app URL'
    loading.value = false
    return
  }

  loading.value = true
  error.value = null

  try {
    const [profileData, appData] = await Promise.all([
      publicAppsService.getUserProfile(publicUsername.value),
      publicAppsService.getAppDetails(publicUsername.value, appId.value),
    ])

    profile.value = profileData
    app.value = appData
    activeImageUrl.value = appData.thumbnailUrl || appData.screenshots[0]?.imageUrl || ''
  } catch (err: unknown) {
    if ((err as { response?: { status?: number } })?.response?.status === 404) {
      error.value = 'App not found'
    } else {
      error.value = 'Failed to load this app right now.'
    }
  } finally {
    loading.value = false
  }
}

function goBackToProfile() {
  router.push({
    name: 'public-apps',
    params: { publicUsername: publicUsername.value },
  })
}

function openApp() {
  if (app.value?.appUrl) {
    window.open(app.value.appUrl, '_blank')
  }
}

async function toggleStar() {
  if (!app.value) return

  if (!authStore.isAuthenticated) {
    toast.add({
      severity: 'info',
      summary: 'Sign in required',
      detail: 'Sign in to star apps you like.',
      life: 3000,
    })
    return
  }

  const nextStarred = !app.value.isStarredByCurrentUser
  const nextCount = Math.max(0, app.value.starCount + (nextStarred ? 1 : -1))

  app.value = {
    ...app.value,
    isStarredByCurrentUser: nextStarred,
    starCount: nextCount,
  }

  try {
    if (nextStarred) {
      await publicAppsService.starApp(app.value.id)
    } else {
      await publicAppsService.unstarApp(app.value.id)
    }
  } catch {
    app.value = {
      ...app.value,
      isStarredByCurrentUser: !nextStarred,
      starCount: Math.max(0, nextCount + (nextStarred ? -1 : 1)),
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
  void loadPage()
})
</script>

<template>
  <div class="public-app-detail-view">
    <header v-if="!authStore.isAuthenticated" class="public-header">
      <div class="header-inner">
        <router-link to="/" class="logo">
          <span class="logo-mark">A</span>
          <span class="logo-text">Automaton Store</span>
        </router-link>
        <nav class="store-nav" aria-label="Store navigation">
          <button @click="goBackToProfile">Creator Store</button>
          <a v-if="app?.appUrl" :href="app.appUrl" target="_blank" rel="noopener">Open App</a>
        </nav>
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

    <main class="detail-main">
      <div v-if="loading" class="loading-state">
        <ProgressSpinner />
        <p>Loading app details...</p>
      </div>

      <div v-else-if="error" class="error-state">
        <i class="pi pi-exclamation-triangle"></i>
        <h2>{{ error }}</h2>
        <Button label="Back to profile" icon="pi pi-arrow-left" @click="goBackToProfile" />
      </div>

      <div v-else-if="app && profile" class="detail-shell">
        <div class="detail-topbar">
          <Button
            label="Back to profile"
            icon="pi pi-arrow-left"
            text
            @click="goBackToProfile"
          />
        </div>

        <section class="product-shell">
          <div class="product-heading">
            <h1>{{ app.name }}</h1>
            <Tag v-if="app.isFeatured" value="Featured" severity="warning" />
          </div>

          <div class="hero-media">
            <img
              v-if="heroImageUrl"
              :src="heroImageUrl"
              :alt="app.name"
              class="hero-image"
            />
            <AppCoverPlaceholder v-else :app-id="app.id" :name="app.name" />
          </div>

          <aside class="hero-copy">
            <div class="creator-row">
              <Avatar
                v-if="profile.avatarUrl"
                :image="profile.avatarUrl"
                size="large"
                shape="circle"
              />
              <Avatar v-else size="large" shape="circle" class="creator-avatar-fallback">
                <template #default>
                  <span>{{ getInitials(profile.displayName || profile.username) }}</span>
                </template>
              </Avatar>
              <div>
                <div class="creator-label">Built by</div>
                <button class="creator-link" @click="goBackToProfile">
                  {{ profile.displayName || profile.username }}
                </button>
              </div>
            </div>

            <div class="meta-row">
              <span v-if="app.category" class="meta-pill">{{ app.category }}</span>
              <span class="meta-stat">
                <i class="pi pi-star-fill"></i> {{ app.starCount }} stars
              </span>
              <span class="meta-stat">
                <i class="pi pi-eye"></i> {{ app.viewCount }} views
              </span>
              <span v-if="app.publishedAt" class="meta-stat">
                <i class="pi pi-calendar"></i> {{ formatPublishedDate(app.publishedAt) }}
              </span>
            </div>

            <p v-if="app.description || app.summary" class="app-description">
              {{ app.description || app.summary }}
            </p>

            <div v-if="app.tags" class="tag-row">
              <Tag
                v-for="tag in parseTags(app.tags)"
                :key="tag"
                :value="tag"
                severity="secondary"
              />
            </div>

            <div class="action-row">
              <Button
                :label="app.isStarredByCurrentUser ? 'Unstar' : 'Star'"
                :icon="app.isStarredByCurrentUser ? 'pi pi-star-fill' : 'pi pi-star'"
                severity="secondary"
                outlined
                @click="toggleStar"
              />
              <Button
                label="Open App"
                icon="pi pi-arrow-up-right"
                icon-pos="right"
                @click="openApp"
              />
            </div>
          </aside>
        </section>

        <section v-if="galleryImages.length > 0" class="gallery-section">
          <div class="section-heading">
            <h2>Gallery</h2>
            <p>Preview the experience before opening the app.</p>
          </div>

          <div class="gallery-grid">
            <button
              v-for="item in galleryImages"
              :key="item.key"
              class="gallery-card"
              :class="{ active: activeImageUrl === item.imageUrl }"
              @click="activeImageUrl = item.imageUrl"
            >
              <img :src="resolveAssetUrl(item.imageUrl)" :alt="`${app.name} preview`" />
              <span>{{ item.label }}</span>
            </button>
          </div>
        </section>
      </div>
    </main>
  </div>
</template>

<style scoped>
.public-app-detail-view {
  min-height: 100vh;
  background:
    radial-gradient(circle at top left, rgba(102, 192, 244, 0.2), transparent 30rem),
    linear-gradient(180deg, #1b2838 0%, #0f1922 32rem, #0b1118 100%);
  color: #c7d5e0;
}

.public-header {
  border-bottom: 1px solid rgba(103, 193, 245, 0.12);
  background: rgba(23, 29, 37, 0.96);
  backdrop-filter: blur(12px);
  position: sticky;
  top: 0;
  z-index: 100;
}

:global(.app-dark) .public-header {
  border-color: var(--p-surface-700);
}

.header-inner,
.detail-shell {
  max-width: 1280px;
  margin: 0 auto;
  padding-left: 1.5rem;
  padding-right: 1.5rem;
}

.header-inner {
  padding-top: 0.75rem;
  padding-bottom: 0.75rem;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
}

.logo {
  display: inline-flex;
  align-items: center;
  gap: 0.6rem;
  text-decoration: none;
}

.logo-mark {
  display: grid;
  place-items: center;
  width: 2rem;
  height: 2rem;
  background: linear-gradient(135deg, #66c0f4, #1a9fff);
  color: #0b1118;
  font-weight: 800;
}

.logo-text {
  font-size: 1.15rem;
  font-weight: 700;
  color: #ffffff;
}

.store-nav {
  display: flex;
  gap: 1rem;
  align-items: center;
}

.store-nav a,
.store-nav button {
  border: 0;
  background: none;
  color: #dcdedf;
  font: inherit;
  text-decoration: none;
  cursor: pointer;
}

.store-nav a:hover,
.store-nav button:hover {
  color: #66c0f4;
}

.header-actions {
  display: flex;
  gap: 0.5rem;
}

.detail-main {
  padding: 2rem 0 3rem;
}

.loading-state,
.error-state {
  min-height: 50vh;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 1rem;
  text-align: center;
}

.error-state i {
  font-size: 3rem;
  color: var(--p-orange-500);
}

.detail-topbar {
  margin-bottom: 1rem;
}

.product-shell {
  display: grid;
  grid-template-columns: minmax(0, 1.35fr) minmax(330px, 0.65fr);
  gap: 1rem;
  padding: 1rem;
  background: rgba(22, 34, 49, 0.9);
  border: 1px solid rgba(103, 193, 245, 0.14);
  box-shadow: 0 18px 50px rgba(0, 0, 0, 0.28);
}

.product-heading {
  grid-column: 1 / -1;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  padding: 0.25rem 0.25rem 0.5rem;
}

.product-heading h1 {
  margin: 0;
  color: #ffffff;
  font-size: clamp(1.8rem, 4vw, 3rem);
  line-height: 1.05;
}

.hero-media {
  position: relative;
  min-height: 320px;
  overflow: hidden;
  background: #0e141b;
}

.hero-image {
  width: 100%;
  height: 100%;
  object-fit: cover;
  display: block;
}

.hero-copy {
  display: flex;
  flex-direction: column;
  gap: 1rem;
  padding: 1rem;
  background: rgba(0, 0, 0, 0.18);
}

.creator-row {
  display: flex;
  align-items: center;
  gap: 0.85rem;
}

.creator-avatar-fallback {
  background: linear-gradient(135deg, var(--p-primary-400) 0%, var(--p-primary-600) 100%);
  color: white;
  font-weight: 700;
}

.creator-label {
  font-size: 0.78rem;
  text-transform: uppercase;
  letter-spacing: 0.08em;
  color: #8f98a0;
}

.creator-link {
  border: none;
  background: none;
  padding: 0;
  font-size: 1rem;
  font-weight: 600;
  color: #ffffff;
  cursor: pointer;
}

.creator-link:hover {
  color: #66c0f4;
}

.meta-row {
  display: flex;
  flex-wrap: wrap;
  gap: 0.65rem;
}

.meta-pill,
.meta-stat {
  display: inline-flex;
  align-items: center;
  gap: 0.35rem;
  padding: 0.45rem 0.7rem;
  border-radius: 999px;
  font-size: 0.9rem;
}

.meta-pill {
  background: #66c0f4;
  color: #0b1118;
  font-weight: 600;
}

.meta-stat {
  background: rgba(103, 193, 245, 0.11);
  color: #c7d5e0;
}

.app-description {
  margin: 0;
  font-size: 1rem;
  line-height: 1.75;
  color: #dbe8f4;
}

.tag-row {
  display: flex;
  flex-wrap: wrap;
  gap: 0.45rem;
}

.action-row {
  display: flex;
  gap: 0.75rem;
  flex-wrap: wrap;
  margin-top: auto;
}

.gallery-section {
  margin-top: 1.5rem;
  padding: 1.25rem 0 0;
}

.section-heading h2 {
  margin: 0 0 0.35rem;
  font-size: 1.4rem;
  color: #ffffff;
  text-transform: uppercase;
}

.section-heading p {
  margin: 0 0 1rem;
  color: #8f98a0;
}

.gallery-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(200px, 1fr));
  gap: 1rem;
}

.gallery-card {
  border: 1px solid rgba(103, 193, 245, 0.12);
  background: rgba(32, 50, 70, 0.72);
  overflow: hidden;
  padding: 0;
  text-align: left;
  cursor: pointer;
  transition: transform 0.18s ease, border-color 0.18s ease, box-shadow 0.18s ease;
}

.gallery-card:hover {
  transform: translateY(-2px);
  border-color: #66c0f4;
  box-shadow: 0 10px 24px rgba(0, 0, 0, 0.22);
}

.gallery-card.active {
  border-color: #66c0f4;
  box-shadow: 0 0 0 2px rgba(102, 192, 244, 0.2);
}

.gallery-card img {
  width: 100%;
  aspect-ratio: 16 / 9;
  object-fit: cover;
  display: block;
}

.gallery-card span {
  display: block;
  padding: 0.8rem 0.9rem 0.95rem;
  font-size: 0.9rem;
  color: #ffffff;
}

@media (max-width: 960px) {
  .product-shell {
    grid-template-columns: 1fr;
  }

  .hero-media {
    min-height: 240px;
  }
}

@media (max-width: 640px) {
  .detail-shell {
    padding-left: 1rem;
    padding-right: 1rem;
  }

  .product-shell {
    padding: 1rem;
  }

  .gallery-grid {
    grid-template-columns: 1fr 1fr;
  }
}
</style>
