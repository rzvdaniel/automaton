<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'
import AppFooter from '@/components/AppFooter.vue'
import AppsDiscoveryStrip from '@/components/AppsDiscoveryStrip.vue'
import MarketplaceCarousel from '@/components/MarketplaceCarousel.vue'
import PublicSiteHeader from '@/components/PublicSiteHeader.vue'
import { publicAppConfig } from '@/config/publicAppConfig'
import type { MarketplaceAppCard, PublicAppsResponse, PublicUserProfile } from '@/types/marketplace'

const route = useRoute()
const router = useRouter()

const profile = ref<PublicUserProfile | null>(null)
const apps = ref<MarketplaceAppCard[]>([])
const loading = ref(true)
const errorMessage = ref('')
const search = ref('')
const activeCategory = ref('')

const publicUsername = computed(() => String(route.params.publicUsername || ''))
const displayName = computed(() => profile.value?.displayName || profile.value?.username || publicUsername.value)
const profileFeaturedApps = computed(() => apps.value.filter((app) => app.isProfileFeatured))
const carouselApps = computed(() => (profileFeaturedApps.value.length > 0 ? profileFeaturedApps.value : apps.value.slice(0, 10)))
const categories = computed(() => {
  const values = apps.value.map((app) => app.category).filter(Boolean) as string[]
  return ['All', ...Array.from(new Set(values))]
})

const filteredApps = computed(() => {
  const query = search.value.trim().toLowerCase()

  return apps.value
    .filter((app) => activeCategory.value === '' || activeCategory.value === 'All' || app.category === activeCategory.value)
    .filter((app) => {
      if (!query) return true
      return (
        app.name.toLowerCase().includes(query) ||
        (app.summary ?? '').toLowerCase().includes(query) ||
        (app.description ?? '').toLowerCase().includes(query) ||
        (app.category ?? '').toLowerCase().includes(query)
      )
    })
})

function resolveAssetUrl(url?: string | null): string {
  if (!url) return ''
  if (url.startsWith('http')) return url
  return `${publicAppConfig.apiBaseUrl}${url.replace(/^\/+/, '')}`
}

function openDetails(appId: string) {
  void router.push(`/${publicUsername.value}/apps/${appId}`)
}

async function loadCreatorApps() {
  loading.value = true
  errorMessage.value = ''

  try {
    const response = await fetch(
      `${publicAppConfig.apiBaseUrl}api/public/${encodeURIComponent(publicUsername.value)}/apps`,
    )

    if (!response.ok) {
      errorMessage.value = response.status === 404 ? 'This creator was not found.' : 'Unable to load this creator store.'
      profile.value = null
      apps.value = []
      return
    }

    const data = (await response.json()) as PublicAppsResponse
    profile.value = data.profile
    apps.value = data.apps
    activeCategory.value = ''
    search.value = ''
  } catch {
    errorMessage.value = 'Unable to load this creator store.'
    profile.value = null
    apps.value = []
  } finally {
    loading.value = false
  }
}

watch(publicUsername, loadCreatorApps)
onMounted(loadCreatorApps)
</script>

<template>
  <div>
    <PublicSiteHeader />
    <AppsDiscoveryStrip />
    <main class="creator-page">
      <div class="store-toolbar">
        <div class="container toolbar-inner">
          <div class="category-tabs" role="tablist" aria-label="Creator app categories">
            <button
              v-for="category in categories"
              :key="category"
              class="category-tab"
              :class="{ 'category-tab-active': activeCategory === category || (!activeCategory && category === 'All') }"
              type="button"
              @click="activeCategory = category"
            >
              {{ category }}
            </button>
          </div>

          <label class="search-bar">
            <i class="pi pi-search"></i>
            <input v-model="search" type="search" placeholder="Search apps" />
          </label>
        </div>
      </div>

      <div class="container">
        <section class="creator-hero">
          <span class="section-label">Creator store</span>
          <h1>{{ displayName }}'s apps</h1>
          <p v-if="profile?.bio">{{ profile.bio }}</p>
          <p v-else>Browse public apps published by this Automaton creator.</p>
        </section>

        <section v-if="loading" class="creator-state">
          <div class="state-orb" />
          <p>Loading creator apps...</p>
        </section>

        <section v-else-if="errorMessage" class="creator-state">
          <div class="state-orb" />
          <p>{{ errorMessage }}</p>
          <RouterLink to="/apps" class="btn btn-secondary">Browse marketplace</RouterLink>
        </section>

        <template v-else>
          <MarketplaceCarousel
            v-if="carouselApps.length > 0"
            class="creator-carousel"
            :apps="carouselApps"
            @select="openDetails"
          />

          <section class="catalog-section">
            <div class="catalog-header">
              <span class="section-label">Browse catalog</span>
              <span>{{ filteredApps.length }} result{{ filteredApps.length === 1 ? '' : 's' }}</span>
            </div>

            <div v-if="filteredApps.length === 0" class="creator-state">
              <div class="state-orb" />
              <p>No apps matched this filter.</p>
            </div>

            <div v-else class="apps-list">
              <article
                v-for="app in filteredApps"
                :key="app.id"
                class="store-card"
                role="link"
                tabindex="0"
                @click="openDetails(app.id)"
                @keydown.enter.prevent="openDetails(app.id)"
                @keydown.space.prevent="openDetails(app.id)"
              >
                <div class="card-media">
                  <img v-if="app.thumbnailUrl" :src="resolveAssetUrl(app.thumbnailUrl)" :alt="app.name" />
                  <span v-else>{{ app.name.slice(0, 1) }}</span>
                </div>
                <div class="card-copy">
                  <h3>{{ app.name }}</h3>
                  <p>{{ app.description || app.summary || 'Published Automaton app' }}</p>
                  <div class="card-meta">
                    <span v-if="app.category" class="tag">{{ app.category }}</span>
                    <span><i class="pi pi-star-fill"></i>{{ app.starCount }}</span>
                    <span><i class="pi pi-eye"></i>{{ app.viewCount }}</span>
                  </div>
                </div>
              </article>
            </div>
          </section>
        </template>
      </div>
    </main>
    <AppFooter />
  </div>
</template>

<style scoped>
.creator-page {
  padding: 0 0 96px;
}

.creator-hero {
  padding: 34px 0 8px;
  margin-bottom: 28px;
}

.creator-hero h1 {
  max-width: 820px;
  margin: 0;
  color: #ffffff;
  font-size: clamp(2rem, 4vw, 3.2rem);
  line-height: 1.05;
}

.creator-hero p {
  max-width: 720px;
  margin-top: 12px;
  color: var(--color-text-muted);
  font-size: 1.05rem;
}

.creator-carousel,
.catalog-section {
  margin-top: 28px;
}

.store-toolbar {
  position: sticky;
  top: 64px;
  z-index: 40;
  padding: 4px 0 0;
  border-bottom: 1px solid rgba(124, 106, 245, 0.14);
  background:
    radial-gradient(circle at 14% 0%, rgba(77, 166, 255, 0.14), transparent 34%),
    radial-gradient(circle at 82% 0%, rgba(124, 106, 245, 0.18), transparent 36%),
    rgba(10, 10, 15, 0.88);
  backdrop-filter: blur(14px);
}

.toolbar-inner {
  display: flex;
  gap: 16px;
  align-items: center;
  justify-content: space-between;
  padding: 6px 8px;
  border: 1px solid rgba(124, 106, 245, 0.18);
  background:
    linear-gradient(135deg, rgba(35, 28, 70, 0.78), rgba(14, 28, 43, 0.78)),
    rgba(17, 17, 24, 0.86);
  box-shadow: 0 12px 26px rgba(0, 0, 0, 0.2);
}

.category-tabs {
  display: flex;
  gap: 6px;
  min-width: 0;
  overflow-x: auto;
}

.category-tab {
  border: 0;
  background: transparent;
  color: #c7d5e0;
  padding: 7px 12px;
  font-weight: 700;
  white-space: nowrap;
  cursor: pointer;
  transition: all 0.2s ease;
}

.category-tab:hover {
  background: rgba(124, 106, 245, 0.16);
  color: #ffffff;
}

.category-tab-active {
  background: linear-gradient(135deg, rgba(124, 106, 245, 0.34), rgba(77, 166, 255, 0.2));
  color: #ffffff;
}

.search-bar {
  display: flex;
  align-items: center;
  gap: 8px;
  width: min(380px, 40vw);
  min-width: 0;
  padding: 5px 8px;
  border: 1px solid rgba(124, 106, 245, 0.18);
  background: rgba(10, 10, 15, 0.66);
  color: var(--color-blue);
}

.search-bar input {
  min-width: 0;
  width: 100%;
  border: 0;
  outline: 0;
  background: transparent;
  color: #ffffff;
  font-size: 0.92rem;
  line-height: 1.2;
}

.catalog-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  margin-bottom: 10px;
  color: var(--color-text-muted);
}

.apps-list {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 12px;
}

.store-card {
  display: flex;
  flex-direction: column;
  min-width: 0;
  background: rgba(22, 34, 49, 0.88);
  border: 1px solid rgba(103, 193, 245, 0.14);
  box-shadow: 0 8px 22px rgba(0, 0, 0, 0.2);
  cursor: pointer;
  transition: transform 0.18s ease, border-color 0.18s ease, background 0.18s ease;
}

.store-card:hover {
  transform: translateY(-2px);
  border-color: rgba(103, 193, 245, 0.42);
  background: rgba(32, 50, 70, 0.96);
}

.card-media {
  display: grid;
  place-items: center;
  aspect-ratio: 1 / 1;
  overflow: hidden;
  padding: 10px;
  background: #05070d;
  color: #66c0f4;
  font-size: 2.5rem;
  font-weight: 800;
}

.card-media img {
  width: 100%;
  height: 100%;
  min-width: 0;
  min-height: 0;
  object-fit: contain;
  display: block;
}

.card-copy {
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 12px 14px;
  flex: 1;
}

.card-copy h3 {
  color: #ffffff;
  font-size: 1rem;
  line-height: 1.25;
}

.card-copy p {
  color: #acb9c7;
  font-size: 0.88rem;
  line-height: 1.38;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
}

.card-meta {
  display: flex;
  align-items: center;
  gap: 10px;
  flex-wrap: wrap;
  margin-top: auto;
  color: #8f98a0;
  font-size: 0.8rem;
}

.card-meta span {
  display: inline-flex;
  align-items: center;
  gap: 5px;
}

.creator-state {
  display: grid;
  place-items: center;
  gap: 14px;
  min-height: 260px;
  padding: 32px;
  border: 1px solid var(--color-border);
  background: var(--color-surface);
  text-align: center;
  color: var(--color-text-muted);
}

.state-orb {
  width: 76px;
  height: 76px;
  border-radius: 999px;
  background:
    radial-gradient(circle at 30% 30%, rgba(255, 255, 255, 0.28), transparent 35%),
    linear-gradient(135deg, rgba(102, 192, 244, 0.4), rgba(77, 166, 255, 0.18));
  box-shadow: 0 18px 50px rgba(102, 192, 244, 0.2);
}

@media (max-width: 980px) {
  .apps-list {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (max-width: 820px) {
  .apps-list {
    grid-template-columns: 1fr;
  }

  .toolbar-inner {
    display: grid;
  }

  .search-bar {
    width: 100%;
  }
}
</style>
