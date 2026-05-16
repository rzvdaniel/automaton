<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import Button from 'primevue/button'
import ProgressSpinner from 'primevue/progressspinner'
import PublicAppCard from '../components/PublicAppCard.vue'
import { publicAppsService } from '../services/publicApps.service'
import { useAuthStore } from '@/features/auth/stores/auth.store'
import type { PublicUserProfile, PublicAppCard as AppCardType } from '../types/publicApps.types'
import { useToast } from 'primevue/usetoast'

const route = useRoute()
const router = useRouter()
const authStore = useAuthStore()
const toast = useToast()

const loading = ref(true)
const error = ref<string | null>(null)
const profile = ref<PublicUserProfile | null>(null)
const apps = ref<AppCardType[]>([])
const searchQuery = ref('')
const selectedCategory = ref('All')
const activeFeaturedIndex = ref(0)
const publicUsername = computed(() => (route.params.publicUsername as string) || '')

const featuredApps = computed(() => apps.value.filter((app) => app.isFeatured))

const activeFeaturedApp = computed(() => featuredApps.value[activeFeaturedIndex.value])

const activeFeaturedScreenshots = computed(() => {
  const app = activeFeaturedApp.value
  if (!app) return []

  const screenshots = (app.screenshots ?? []).slice(0, 4).map((screenshot) => ({
    key: screenshot.id,
    imageUrl: screenshot.imageUrl,
    label: screenshot.caption || app.name,
  }))

  if (screenshots.length > 0) {
    return screenshots
  }

  return app.thumbnailUrl
    ? [
        {
          key: 'thumbnail',
          imageUrl: app.thumbnailUrl,
          label: app.name,
        },
      ]
    : []
})

const categories = computed(() => {
  const uniqueCategories = new Set(apps.value.map((app) => app.category).filter(Boolean))
  return ['All', ...Array.from(uniqueCategories)] as string[]
})

const filteredApps = computed(() => {
  const query = searchQuery.value.trim().toLowerCase()

  return apps.value
    .filter((app) => {
      const matchesCategory = selectedCategory.value === 'All' || app.category === selectedCategory.value
      const matchesSearch =
        !query ||
        app.name.toLowerCase().includes(query) ||
        (app.summary ?? '').toLowerCase().includes(query) ||
        (app.category ?? '').toLowerCase().includes(query)

      return matchesCategory && matchesSearch
    })
    .sort((a, b) => Number(b.isFeatured) - Number(a.isFeatured) || b.starCount - a.starCount)
})

const newAndTrendingApps = computed(() =>
  [...apps.value]
    .sort((a, b) => {
      const bDate = b.publishedAt ? new Date(b.publishedAt).getTime() : 0
      const aDate = a.publishedAt ? new Date(a.publishedAt).getTime() : 0
      return bDate - aDate || b.viewCount - a.viewCount
    })
    .slice(0, 6),
)

const displayName = computed(() => profile.value?.displayName || profile.value?.username || 'Automaton')

function resolveAppAssetUrl(url?: string | null): string {
  if (!url) return ''
  if (url.startsWith('http')) return url
  return `${import.meta.env.VITE_API_BASE_URL || ''}${url}`
}

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

function openDetails(app: AppCardType) {
  router.push({
    name: 'public-app-detail',
    params: {
      publicUsername: publicUsername.value,
      appId: app.id,
    },
  })
}

function setActiveFeatured(index: number) {
  activeFeaturedIndex.value = index
}

function showPreviousFeatured() {
  if (featuredApps.value.length === 0) return
  activeFeaturedIndex.value =
    (activeFeaturedIndex.value - 1 + featuredApps.value.length) % featuredApps.value.length
}

function showNextFeatured() {
  if (featuredApps.value.length === 0) return
  activeFeaturedIndex.value = (activeFeaturedIndex.value + 1) % featuredApps.value.length
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

  try {
    if (nextStarred) {
      await publicAppsService.starApp(app.id)
    } else {
      await publicAppsService.unstarApp(app.id)
    }
  } catch {
    app.isStarredByCurrentUser = !nextStarred
    app.starCount = Math.max(0, app.starCount + (nextStarred ? -1 : 1))

    toast.add({
      severity: 'error',
      summary: 'Star failed',
      detail: 'Could not update your star right now.',
      life: 3000,
    })
  }
}

onMounted(() => {
  void loadUserApps()
})
</script>

<template>
  <div class="public-apps-view">
    <header v-if="!authStore.isAuthenticated" class="public-header">
      <div class="header-inner">
        <router-link to="/" class="logo">
          <span class="logo-mark">A</span>
          <span class="logo-text">Automaton Store</span>
        </router-link>
        <nav class="store-nav" aria-label="Store navigation">
          <a href="#featured">Featured</a>
          <a href="#browse">Browse</a>
          <a href="#trending">Trending</a>
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

    <main class="public-main">
      <div v-if="loading" class="loading-state">
        <ProgressSpinner />
        <p>Loading...</p>
      </div>

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

      <template v-else-if="profile">
        <div class="store-toolbar">
          <div class="toolbar-inner">
            <div class="browse-tabs" aria-label="Categories">
              <button
                v-for="category in categories"
                :key="category"
                class="category-tab"
                :class="{ active: selectedCategory === category }"
                @click="selectedCategory = category"
              >
                {{ category }}
              </button>
            </div>
            <label class="search-box">
              <i class="pi pi-search"></i>
              <input v-model="searchQuery" type="search" placeholder="Search apps" />
            </label>
          </div>
        </div>

        <section class="store-hero">
          <div class="hero-inner">
            <div class="hero-copy">
              <span class="eyebrow">Automaton creator store</span>
              <h1>{{ displayName }}'s apps</h1>
              <p>
                Discover workflow-powered web apps, tools, and experiments published by this
                creator.
              </p>
            </div>
          </div>
        </section>

        <div class="apps-section">
          <section
            v-if="featuredApps.length > 0 && activeFeaturedApp"
            id="featured"
            class="featured-section"
          >
            <div class="section-header">
              <div>
                <span class="section-kicker">Featured and recommended</span>
                <h2>Spotlight apps</h2>
              </div>
            </div>

            <div class="featured-carousel">
              <button
                v-if="featuredApps.length > 1"
                class="carousel-arrow carousel-arrow-left"
                type="button"
                aria-label="Previous featured app"
                @click="showPreviousFeatured"
              >
                <i class="pi pi-chevron-left"></i>
              </button>

              <button
                class="featured-slide"
                type="button"
                @click="openDetails(activeFeaturedApp)"
              >
                <div class="featured-main-panel">
                  <div class="featured-main-media">
                    <img
                      v-if="activeFeaturedApp.thumbnailUrl"
                      :src="resolveAppAssetUrl(activeFeaturedApp.thumbnailUrl)"
                      :alt="activeFeaturedApp.name"
                    />
                    <div v-else class="featured-placeholder">
                      {{ activeFeaturedApp.name.slice(0, 1) }}
                    </div>
                  </div>

                  <div class="featured-main-copy">
                    <h3>{{ activeFeaturedApp.name }}</h3>
                    <div class="featured-stats">
                      <span><i class="pi pi-star-fill"></i>{{ activeFeaturedApp.starCount }}</span>
                      <span><i class="pi pi-eye"></i>{{ activeFeaturedApp.viewCount }}</span>
                    </div>
                    <p>{{ activeFeaturedApp.summary || 'Published Automaton app' }}</p>
                  </div>
                </div>

                <aside class="featured-info">
                  <div v-if="activeFeaturedScreenshots.length > 0" class="featured-preview-grid">
                    <div
                      v-for="screenshot in activeFeaturedScreenshots"
                      :key="screenshot.key"
                      class="featured-preview"
                    >
                      <img :src="resolveAppAssetUrl(screenshot.imageUrl)" :alt="screenshot.label" />
                    </div>
                  </div>
                </aside>
              </button>

              <button
                v-if="featuredApps.length > 1"
                class="carousel-arrow carousel-arrow-right"
                type="button"
                aria-label="Next featured app"
                @click="showNextFeatured"
              >
                <i class="pi pi-chevron-right"></i>
              </button>
            </div>

            <div v-if="featuredApps.length > 1" class="carousel-dots" aria-label="Featured app slides">
              <button
                v-for="(app, index) in featuredApps"
                :key="app.id"
                class="carousel-dot"
                :class="{ active: index === activeFeaturedIndex }"
                type="button"
                :aria-label="`Show ${app.name}`"
                @click="setActiveFeatured(index)"
              ></button>
            </div>
          </section>

          <section id="browse" class="browse-section">
            <div class="section-header">
              <div>
                <span class="section-kicker">Browse catalog</span>
                <h2>Published apps</h2>
              </div>
              <span class="result-count">
                {{ filteredApps.length }} result{{ filteredApps.length === 1 ? '' : 's' }}
              </span>
            </div>

          <div v-if="apps.length === 0" class="empty-state">
            <i class="pi pi-inbox"></i>
            <h3>No public apps yet</h3>
            <p>This user hasn't published any apps yet. Check back later!</p>
          </div>

          <div v-else-if="filteredApps.length === 0" class="empty-state">
            <i class="pi pi-search"></i>
            <h3>No matches found</h3>
            <p>Try a different search or category.</p>
          </div>

          <div v-else class="apps-grid">
            <PublicAppCard
              v-for="app in filteredApps"
              :key="app.id"
              :app="app"
              @details="openDetails"
              @toggle-star="toggleStar"
            />
          </div>
          </section>

          <section v-if="newAndTrendingApps.length > 0" id="trending" class="trending-section">
            <div class="section-header">
              <div>
                <span class="section-kicker">New and trending</span>
                <h2>Recently updated</h2>
              </div>
            </div>

            <div class="trending-list">
              <button
                v-for="app in newAndTrendingApps"
                :key="app.id"
                class="trending-item"
                @click="openDetails(app)"
              >
                <span>{{ app.name }}</span>
                <small>{{ app.category || 'App' }} - {{ app.viewCount }} views</small>
              </button>
            </div>
          </section>
        </div>
      </template>
    </main>

    <footer class="public-footer">
      <div class="footer-inner">
        <p>
          Built with <strong>Automaton</strong> -
          <router-link to="/register">Create your own apps</router-link>
        </p>
      </div>
    </footer>
  </div>
</template>

<style scoped>
.public-apps-view {
  min-height: 100vh;
  display: flex;
  flex-direction: column;
  background:
    radial-gradient(circle at top left, rgba(102, 192, 244, 0.24), transparent 28rem),
    linear-gradient(180deg, #1b2838 0%, #0f1922 32rem, #0b1118 100%);
  color: #c7d5e0;
}

.public-header {
  background: rgba(23, 29, 37, 0.96);
  border-bottom: 1px solid rgba(103, 193, 245, 0.12);
  position: sticky;
  top: 0;
  z-index: 100;
  backdrop-filter: blur(12px);
}

:global(.app-dark) .public-header {
  border-color: var(--p-surface-700);
}

.header-inner {
  max-width: 1280px;
  margin: 0 auto;
  padding: 0.75rem 1.5rem;
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
  align-items: center;
  gap: 1rem;
  font-size: 0.9rem;
}

.store-nav a {
  color: #dcdedf;
  text-decoration: none;
}

.store-nav a:hover {
  color: #66c0f4;
}

.header-actions {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.public-main {
  flex: 1;
  padding-bottom: 2rem;
}

.store-hero {
  padding: 2.2rem 1.5rem 1.15rem;
}

.hero-inner {
  max-width: 1280px;
  margin: 0 auto;
}

.eyebrow,
.section-kicker {
  color: #66c0f4;
  font-size: 0.78rem;
  font-weight: 700;
  letter-spacing: 0.12em;
  text-transform: uppercase;
}

.hero-copy h1 {
  margin: 0.35rem 0 0.75rem;
  color: #ffffff;
  font-size: clamp(2.2rem, 5vw, 4.4rem);
  line-height: 1;
}

.hero-copy p {
  max-width: 680px;
  margin: 0;
  color: #acb9c7;
  font-size: 1.05rem;
}

.loading-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 4rem 2rem;
  color: #acb9c7;
}

.loading-state p {
  margin-top: 1rem;
}

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
  color: #ffcc6a;
  margin-bottom: 1rem;
}

.error-state h2 {
  margin: 0 0 0.5rem 0;
  color: #ffffff;
}

.error-state p {
  margin: 0 0 1.5rem 0;
  color: #acb9c7;
}

.store-toolbar {
  position: sticky;
  top: 57px;
  z-index: 50;
  padding: 0.4rem 1.5rem 0;
  background: rgba(16, 30, 43, 0.72);
  border-bottom: 1px solid rgba(103, 193, 245, 0.08);
  backdrop-filter: blur(10px);
}

.toolbar-inner {
  max-width: 1280px;
  margin: 0 auto;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  padding: 0.35rem;
  background: rgba(35, 55, 75, 0.78);
  box-shadow: 0 8px 18px rgba(0, 0, 0, 0.18);
}

.browse-tabs {
  display: flex;
  gap: 0.25rem;
  overflow-x: auto;
}

.category-tab {
  border: 0;
  background: transparent;
  color: #c7d5e0;
  padding: 0.45rem 0.65rem;
  font-weight: 600;
  white-space: nowrap;
  cursor: pointer;
}

.category-tab.active,
.category-tab:hover {
  background: rgba(103, 193, 245, 0.14);
  color: #ffffff;
}

.search-box {
  min-width: min(320px, 40vw);
  display: flex;
  align-items: center;
  gap: 0.5rem;
  background: rgba(14, 20, 27, 0.72);
  border: 1px solid rgba(103, 193, 245, 0.14);
  padding: 0.38rem 0.6rem;
  color: #66c0f4;
}

.search-box input {
  width: 100%;
  border: 0;
  outline: 0;
  background: transparent;
  color: #ffffff;
}

.search-box input::placeholder {
  color: #9fb6ca;
}

.apps-section {
  max-width: 1280px;
  margin: 0 auto;
  padding: 1.5rem;
}

.section-header {
  margin: 0 0 1rem;
  display: flex;
  align-items: end;
  justify-content: space-between;
  gap: 1rem;
}

.section-header h2 {
  margin: 0.25rem 0 0;
  font-size: 1.5rem;
  font-weight: 600;
  color: #ffffff;
  text-transform: uppercase;
}

.result-count {
  color: #8f98a0;
  font-size: 0.9rem;
}

.featured-section,
.browse-section,
.trending-section {
  margin-bottom: 2rem;
}

.featured-carousel {
  position: relative;
  margin: 0 auto;
  padding: 1rem;
  background: rgba(38, 45, 56, 0.92);
  box-shadow: 0 0 0 1px rgba(0, 0, 0, 0.3), 0 18px 38px rgba(0, 0, 0, 0.45);
}

.featured-slide {
  width: 100%;
  height: 400px;
  display: grid;
  grid-template-columns: minmax(360px, 0.78fr) minmax(0, 1.22fr);
  padding: 0;
  overflow: hidden;
  border: 0;
  background: #2f3846;
  box-shadow: inset 0 0 0 1px rgba(255, 255, 255, 0.04);
  cursor: pointer;
  text-align: left;
}

.featured-main-panel {
  min-width: 0;
  height: 400px;
  display: flex;
  flex-direction: column;
  background: #333b49;
}

.featured-main-media {
  height: 255px;
  flex: 0 0 255px;
  background: #0e141b;
}

.featured-main-media img,
.featured-placeholder {
  width: 100%;
  height: 100%;
  object-fit: cover;
  display: block;
}

.featured-placeholder {
  display: grid;
  place-items: center;
  color: #66c0f4;
  font-size: 4rem;
  font-weight: 800;
}

.featured-main-copy {
  min-height: 0;
  padding: 0.8rem 0.9rem 0.9rem;
  display: flex;
  flex-direction: column;
  gap: 0.65rem;
}

.featured-main-copy h3 {
  margin: 0;
  color: #ffffff;
  font-size: 1.25rem;
  font-weight: 500;
  line-height: 1.2;
}

.featured-main-copy p {
  margin: 0;
  color: #dbe8f4;
  font-size: 0.92rem;
  line-height: 1.45;
  display: -webkit-box;
  -webkit-line-clamp: 3;
  -webkit-box-orient: vertical;
  overflow: hidden;
}

.featured-info {
  min-width: 0;
  height: 400px;
  padding: 0 0 0 0.9rem;
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  background: #2a3340;
}

.featured-preview-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  grid-template-rows: 1fr 1fr;
  gap: 0.45rem;
  min-height: 0;
  flex: 1;
}

.featured-preview {
  min-height: 0;
  overflow: hidden;
  display: grid;
  place-items: center;
  background: rgba(103, 193, 245, 0.1);
  border: 1px solid transparent;
  color: #66c0f4;
  font-weight: 800;
}

.featured-preview.active {
  border-color: rgba(103, 193, 245, 0.7);
}

.featured-preview img {
  width: 100%;
  height: 100%;
  object-fit: cover;
}

.featured-stats {
  display: flex;
  gap: 0.45rem;
  color: #8f98a0;
  font-size: 0.85rem;
}

.featured-stats span {
  display: inline-flex;
  align-items: center;
  gap: 0.3rem;
}

.carousel-arrow {
  position: absolute;
  top: 50%;
  z-index: 2;
  width: 3.4rem;
  height: 7rem;
  transform: translateY(-50%);
  border: 0;
  background: linear-gradient(90deg, rgba(103, 193, 245, 0.28), rgba(103, 193, 245, 0));
  color: #ffffff;
  cursor: pointer;
  font-size: 2rem;
}

.carousel-arrow-left {
  left: -3.4rem;
}

.carousel-arrow-right {
  right: -3.4rem;
  background: linear-gradient(270deg, rgba(103, 193, 245, 0.28), rgba(103, 193, 245, 0));
}

.carousel-arrow:hover {
  color: #66c0f4;
}

.carousel-dots {
  display: flex;
  justify-content: center;
  gap: 0.35rem;
  margin-top: 0.75rem;
}

.carousel-dot {
  width: 1rem;
  height: 0.55rem;
  border: 0;
  border-radius: 999px;
  background: rgba(143, 152, 160, 0.55);
  cursor: pointer;
}

.carousel-dot.active {
  background: #66c0f4;
}

.empty-state {
  text-align: center;
  padding: 4rem 2rem;
  color: #8f98a0;
}

.empty-state i {
  font-size: 4rem;
  margin-bottom: 1rem;
  opacity: 0.5;
}

.empty-state h3 {
  margin: 0 0 0.5rem 0;
  color: #ffffff;
}

.empty-state p {
  margin: 0;
}

.apps-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(390px, 1fr));
  gap: 0.75rem;
  padding-bottom: 1rem;
}

.trending-list {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(240px, 1fr));
  gap: 0.65rem;
}

.trending-item {
  border: 1px solid rgba(103, 193, 245, 0.1);
  background: rgba(32, 50, 70, 0.72);
  color: #ffffff;
  padding: 0.85rem 1rem;
  text-align: left;
  cursor: pointer;
}

.trending-item:hover {
  background: linear-gradient(90deg, rgba(103, 193, 245, 0.24), rgba(32, 50, 70, 0.9));
}

.trending-item span,
.trending-item small {
  display: block;
}

.trending-item small {
  margin-top: 0.25rem;
  color: #acb9c7;
}

.public-footer {
  border-top: 1px solid rgba(103, 193, 245, 0.12);
  background: #171d25;
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
  color: #8f98a0;
  font-size: 0.9rem;
}

.footer-inner a {
  color: #66c0f4;
  text-decoration: none;
}

.footer-inner a:hover {
  text-decoration: underline;
}

@media (max-width: 768px) {
  .hero-inner,
  .toolbar-inner {
    grid-template-columns: 1fr;
  }

  .hero-inner,
  .toolbar-inner,
  .header-inner {
    align-items: stretch;
  }

  .store-nav {
    display: none;
  }

  .toolbar-inner {
    flex-direction: column;
  }

  .search-box {
    width: 100%;
  }

  .featured-slide {
    grid-template-columns: 1fr;
    height: auto;
  }

  .featured-main-panel,
  .featured-info {
    height: auto;
  }

  .featured-main-media {
    height: 230px;
    flex-basis: 230px;
  }

  .carousel-arrow {
    width: 2.8rem;
    height: 4.8rem;
    font-size: 1.6rem;
  }

  .carousel-arrow-left {
    left: 0;
  }

  .carousel-arrow-right {
    right: 0;
  }

  .apps-grid {
    grid-template-columns: 1fr;
  }
}

@media (max-width: 600px) {
  .store-hero,
  .store-toolbar,
  .apps-section {
    padding-left: 1rem;
    padding-right: 1rem;
  }

  .featured-info {
    padding: 0.75rem;
  }

  .featured-preview-grid {
    display: none;
  }
}
</style>
