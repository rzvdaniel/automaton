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
  return apps.value
    .filter((app) => activeCategory.value === '' || activeCategory.value === 'All' || app.category === activeCategory.value)
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
      <div class="container">
        <section class="creator-hero">
          <span class="section-label creator-eyebrow">Public creator</span>
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
          <section v-if="carouselApps.length > 0" class="creator-carousel-section">
            <div class="catalog-header">
              <div>
                <h2>Featured & Recommended</h2>
              </div>
            </div>

            <MarketplaceCarousel
              class="creator-carousel"
              :apps="carouselApps"
              title=""
              @select="openDetails"
            />
          </section>

          <section class="catalog-section">
            <div class="catalog-header">
              <div>
                <h2>Browse this creator's catalog</h2>
              </div>
              <span class="catalog-count">{{ filteredApps.length }} result{{ filteredApps.length === 1 ? '' : 's' }}</span>
            </div>

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
  background:
    radial-gradient(circle at top left, rgba(62, 175, 255, 0.08), transparent 28%),
    radial-gradient(circle at top right, rgba(69, 230, 138, 0.07), transparent 30%),
    #06080d;
}

.creator-hero {
  padding: 22px 0 8px;
  margin-bottom: 30px;
}

.creator-eyebrow {
  width: fit-content;
  max-width: 100%;
  gap: 8px;
  padding: 6px 10px;
  margin-bottom: 12px;
  border: 1px solid rgba(80, 215, 255, 0.22);
  border-radius: 999px;
  background: rgba(80, 215, 255, 0.08);
  color: #c4f2ff;
  font-size: 0.82rem;
  font-weight: 700;
}

.creator-hero h1 {
  width: max-content;
  max-width: 100%;
  margin: 0;
  color: #f5f7fb;
  font-size: clamp(3rem, 6.1vw, 6rem);
  line-height: 0.92;
  letter-spacing: -0.06em;
}

.creator-hero p {
  max-width: 720px;
  margin-top: 12px;
  color: #9ba3b4;
  font-size: 1rem;
  line-height: 1.6;
}

.creator-carousel-section,
.creator-carousel,
.catalog-section {
  margin-top: 28px;
}

.category-tabs {
  display: flex;
  gap: 6px;
  min-width: 0;
  overflow-x: auto;
  margin-bottom: 18px;
}

.category-tab {
  padding: 10px 14px;
  border: 1px solid rgba(255, 255, 255, 0.12);
  border-radius: 999px;
  background: rgba(255, 255, 255, 0.04);
  color: #c7d5e0;
  font-size: 0.9rem;
  font-weight: 800;
  white-space: nowrap;
  cursor: pointer;
  transition: all 0.2s ease;
}

.category-tab:hover {
  border-color: rgba(69, 230, 138, 0.28);
  background: rgba(255, 255, 255, 0.06);
  color: #ffffff;
}

.category-tab-active {
  border-color: rgba(69, 230, 138, 0.34);
  background: rgba(69, 230, 138, 0.12);
  color: #ffffff;
}

.catalog-header {
  display: flex;
  align-items: end;
  justify-content: space-between;
  gap: 24px;
  margin-bottom: 14px;
  color: #9ba3b4;
}

.catalog-header h2 {
  margin: 6px 0 0;
  color: #f5f7fb;
  font-size: 1.55rem;
  line-height: 1.15;
  letter-spacing: -0.03em;
}

.catalog-count {
  font-size: 0.92rem;
  font-weight: 700;
}

.apps-list {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 16px;
}

.store-card {
  display: flex;
  flex-direction: column;
  min-width: 0;
  overflow: hidden;
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 8px;
  background: rgba(255, 255, 255, 0.04);
  cursor: pointer;
  transition: transform 0.18s ease, border-color 0.18s ease, background 0.18s ease;
}

.store-card:hover {
  transform: translateY(-3px);
  border-color: rgba(69, 230, 138, 0.34);
  background: rgba(255, 255, 255, 0.06);
}

.card-media {
  display: grid;
  place-items: center;
  aspect-ratio: 16 / 10;
  overflow: hidden;
  background: #05070d;
  color: rgba(255, 255, 255, 0.86);
  font-size: 2.5rem;
  font-weight: 800;
}

.card-media img {
  width: 100%;
  height: 100%;
  min-width: 0;
  min-height: 0;
  object-fit: cover;
  display: block;
}

.card-copy {
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 10px;
  padding: 16px;
  flex: 1;
}

.card-copy h3 {
  margin: 0;
  color: #f5f7fb;
  font-size: 1.03rem;
  line-height: 1.12;
  letter-spacing: -0.018em;
}

.card-copy p {
  margin: 0;
  color: #9ba3b4;
  font-size: 0.88rem;
  line-height: 1.5;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
}

.card-meta {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
  margin-top: auto;
  color: #9fb0c2;
  font-size: 0.78rem;
  font-weight: 850;
}

.card-meta span {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  color: #778196;
}

.creator-state {
  display: grid;
  place-items: center;
  gap: 14px;
  min-height: 260px;
  padding: 32px;
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 8px;
  background: rgba(255, 255, 255, 0.04);
  text-align: center;
  color: #9ba3b4;
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
  .creator-hero h1 {
    width: 100%;
  }

  .apps-list {
    grid-template-columns: 1fr;
  }
}
</style>
