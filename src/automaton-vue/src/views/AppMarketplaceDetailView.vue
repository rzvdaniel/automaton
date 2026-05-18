<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import AppFooter from '@/components/AppFooter.vue'
import AppsDiscoveryStrip from '@/components/AppsDiscoveryStrip.vue'
import PublicSiteHeader from '@/components/PublicSiteHeader.vue'
import { publicAppConfig } from '@/config/publicAppConfig'
import type { MarketplaceAppCard, MarketplaceAppDetails } from '@/types/marketplace'

const route = useRoute()
const marketplaceApiBaseUrl = `${publicAppConfig.apiBaseUrl}api/public/marketplace`

const app = ref<MarketplaceAppDetails | null>(null)
const creatorApps = ref<MarketplaceAppCard[]>([])
const loading = ref(true)
const selectedImageUrl = ref('')
const errorMessage = ref('')

function resolveAssetUrl(url?: string | null): string {
  if (!url) {
    return ''
  }

  if (
    url.startsWith('http') ||
    url.startsWith('data:') ||
    url.startsWith('blob:') ||
    url.startsWith('/assets/') ||
    url.startsWith('/src/')
  ) {
    return url
  }

  return `${publicAppConfig.apiBaseUrl}${url.replace(/^\/+/, '')}`
}

const galleryImages = computed(() => {
  if (!app.value) {
    return []
  }

  const images = [
    ...(app.value.thumbnailUrl
      ? [
          {
            id: `${app.value.id}-cover`,
            imageUrl: app.value.thumbnailUrl,
            caption: 'Cover image',
          },
        ]
      : []),
    ...app.value.screenshots.map((screenshot) => ({
      id: screenshot.id,
      imageUrl: screenshot.imageUrl,
      caption: screenshot.caption || 'Product screenshot',
    })),
  ]

  return images.filter((image, index, collection) =>
    collection.findIndex((candidate) => candidate.imageUrl === image.imageUrl) === index,
  )
})

const tagList = computed(() =>
  app.value?.tags
    ?.split(',')
    .map((tag) => tag.trim())
    .filter(Boolean) ?? [],
)

const launchUrl = computed(() =>
  app.value?.publicUrl ? `${publicAppConfig.studioUrl}web/${app.value.publicUrl}` : null,
)

const forkUrl = computed(() =>
  app.value ? `${publicAppConfig.studioUrl}apps/fork/${app.value.id}` : publicAppConfig.studioUrl,
)

const publishedLabel = computed(() => {
  if (!app.value?.publishedAt) {
    return null
  }

  return new Date(app.value.publishedAt).toLocaleDateString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
  })
})

const relatedHeading = computed(() => {
  const name = app.value?.creatorDisplayName || app.value?.creatorPublicUsername
  return name ? `More apps from ${name}` : 'More apps from this author'
})

function formatCount(value: number) {
  if (value >= 1000) return `${(value / 1000).toFixed(value >= 10000 ? 0 : 1)}k`
  return String(value)
}

const selectedImageIndex = computed(() =>
  galleryImages.value.findIndex((image) => image.imageUrl === selectedImageUrl.value),
)

function selectImage(imageUrl: string) {
  selectedImageUrl.value = imageUrl
}

function previousImage() {
  if (galleryImages.value.length === 0) return

  const index = selectedImageIndex.value >= 0 ? selectedImageIndex.value : 0
  const previousIndex = (index - 1 + galleryImages.value.length) % galleryImages.value.length
  const image = galleryImages.value[previousIndex]
  if (!image) return
  selectedImageUrl.value = image.imageUrl
}

function nextImage() {
  if (galleryImages.value.length === 0) return

  const index = selectedImageIndex.value >= 0 ? selectedImageIndex.value : 0
  const nextIndex = (index + 1) % galleryImages.value.length
  const image = galleryImages.value[nextIndex]
  if (!image) return
  selectedImageUrl.value = image.imageUrl
}

async function loadCreatorApps(appId: string) {
  creatorApps.value = []

  try {
    const response = await fetch(`${marketplaceApiBaseUrl}/apps/${appId}/creator-apps`)

    if (!response.ok) {
      return
    }

    creatorApps.value = await response.json()
  } catch {
    creatorApps.value = []
  }
}

async function loadApp() {
  loading.value = true
  errorMessage.value = ''
  creatorApps.value = []

  try {
    const response = await fetch(`${marketplaceApiBaseUrl}/apps/${route.params.appId}`)

    if (!response.ok) {
      errorMessage.value = response.status === 404 ? 'This app is no longer available in the marketplace.' : 'Unable to load this app right now.'
      app.value = null
      selectedImageUrl.value = ''
      return
    }

    app.value = await response.json()
    selectedImageUrl.value = galleryImages.value[0]?.imageUrl || ''
    await loadCreatorApps(String(route.params.appId))
  } catch {
    errorMessage.value = 'Unable to load this app right now.'
    app.value = null
    selectedImageUrl.value = ''
    creatorApps.value = []
  } finally {
    loading.value = false
  }
}

watch(
  () => route.params.appId,
  async () => {
    await loadApp()
  },
)

onMounted(loadApp)
</script>

<template>
  <div class="app-detail-v2">
    <PublicSiteHeader />
    <AppsDiscoveryStrip />
    <main class="detail-page">
      <div class="detail-shell">
        <RouterLink to="/apps" class="back-link">
          <i class="pi pi-arrow-left" />
          Back to apps
        </RouterLink>

        <section v-if="loading" class="detail-state">
          <div class="state-orb" />
          <p>Loading app details...</p>
        </section>

        <section v-else-if="errorMessage" class="detail-state">
          <div class="state-orb state-orb-error" />
          <p>{{ errorMessage }}</p>
          <RouterLink to="/apps" class="btn btn-secondary">Browse apps</RouterLink>
        </section>

        <template v-else-if="app">
          <section class="detail-hero">
            <div class="summary-panel">
              <div>
                <span class="detail-eyebrow">{{ app.category || 'Live app' }}</span>
                <h1 class="hero-title">{{ app.name }}</h1>
                <div v-if="app.creatorPublicUsername" class="developer-row">
                  by
                  <RouterLink :to="`/${app.creatorPublicUsername}`" class="developer-link">
                    {{ app.creatorDisplayName || app.creatorPublicUsername }}
                  </RouterLink>
                </div>

                <p v-if="app.description" class="hero-description">{{ app.description }}</p>
                <p v-else-if="app.summary" class="hero-description">{{ app.summary }}</p>

                <div class="hero-stats" aria-label="App activity">
                  <span>{{ formatCount(app.viewCount) }} plays</span>
                  <span>{{ formatCount(app.forkCount) }} forks</span>
                  <span>{{ formatCount(app.starCount) }} saves</span>
                  <span v-if="publishedLabel">published {{ publishedLabel }}</span>
                </div>

                <div class="hero-actions">
                  <a v-if="launchUrl" :href="launchUrl" target="_blank" rel="noopener" class="detail-button primary">
                    Open app
                    <i class="pi pi-external-link" />
                  </a>
                  <RouterLink to="/apps" class="detail-button">Browse apps</RouterLink>
                </div>
              </div>

              <div class="meta-strip">
                <div v-if="app.category" class="meta-card"><strong>{{ app.category }}</strong><span>category</span></div>
                <div v-if="publishedLabel" class="meta-card"><strong>{{ publishedLabel }}</strong><span>published</span></div>
                <div class="meta-card"><strong>{{ galleryImages.length }}</strong><span>screenshot{{ galleryImages.length === 1 ? '' : 's' }}</span></div>
              </div>
            </div>

            <article class="detail-gallery">
              <div class="gallery-stage">
                <button
                  v-if="galleryImages.length > 1"
                  type="button"
                  class="gallery-arrow gallery-arrow-left"
                  aria-label="Previous screenshot"
                  @click="previousImage"
                >
                  <i class="pi pi-chevron-left" />
                </button>

                <img
                  v-if="selectedImageUrl"
                  :src="resolveAssetUrl(selectedImageUrl)"
                  :alt="app.name"
                  class="stage-image"
                />
                <div v-else class="stage-image stage-fallback">
                  <span>{{ app.name.charAt(0) }}</span>
                </div>

                <button
                  v-if="galleryImages.length > 1"
                  type="button"
                  class="gallery-arrow gallery-arrow-right"
                  aria-label="Next screenshot"
                  @click="nextImage"
                >
                  <i class="pi pi-chevron-right" />
                </button>
              </div>

              <div v-if="galleryImages.length > 1" class="gallery-strip">
                <button
                  v-for="image in galleryImages"
                  :key="image.id"
                  type="button"
                  class="gallery-thumb"
                  :class="{ 'gallery-thumb-active': selectedImageUrl === image.imageUrl }"
                  @click="selectImage(image.imageUrl)"
                >
                  <img :src="resolveAssetUrl(image.imageUrl)" :alt="image.caption" />
                </button>
              </div>
            </article>
          </section>

          <section class="detail-body-grid">
            <article class="detail-card">
              <h2>About this app</h2>
              <p>{{ app.summary || app.description || 'Open the app to see what this public Automaton build does.' }}</p>
              <div v-if="tagList.length" class="tag-row">
                <span v-for="tag in tagList" :key="tag" class="tag">{{ tag }}</span>
              </div>
            </article>

            <aside class="detail-card fork-card">
              <h3>Fork this idea</h3>
              <p>Use this app as a starting point for a new Automaton build.</p>
              <a :href="forkUrl" class="fork-button" target="_blank" rel="noopener">
                <svg viewBox="0 0 16 16" aria-hidden="true" focusable="false">
                  <path d="M5 3.25a2.25 2.25 0 1 0-1.5 2.122v5.256a2.251 2.251 0 1 0 1.5 0V9.372a2.251 2.251 0 0 0 1.746-1.554h2.628a2.251 2.251 0 1 0 0-1.5H6.746A2.251 2.251 0 0 0 5 4.628V3.25Z" fill="currentColor" />
                </svg>
                Fork app
              </a>
            </aside>
          </section>

          <section v-if="creatorApps.length" class="related-section">
            <div class="related-header">
              <h2 class="related-title">{{ relatedHeading }}</h2>
            </div>

            <div class="related-grid">
              <RouterLink v-for="relatedApp in creatorApps" :key="relatedApp.id" :to="`/apps/${relatedApp.id}`" class="related-card">
                <div class="related-media">
                  <img
                    v-if="relatedApp.thumbnailUrl"
                    :src="resolveAssetUrl(relatedApp.thumbnailUrl)"
                    :alt="relatedApp.name"
                    class="related-image"
                  />
                  <div v-else class="related-image related-fallback">
                    <span>{{ relatedApp.name.charAt(0) }}</span>
                  </div>
                </div>
                <div class="related-body">
                  <h3 class="related-name">{{ relatedApp.name }}</h3>
                  <div v-if="relatedApp.creatorPublicUsername" class="related-creator">
                    by {{ relatedApp.creatorDisplayName || relatedApp.creatorPublicUsername }}
                  </div>
                  <p class="related-summary">
                    {{ relatedApp.summary || relatedApp.description || 'Open the detail page to see what this app does.' }}
                  </p>
                </div>
              </RouterLink>
            </div>
          </section>
        </template>
      </div>
    </main>
    <AppFooter />
  </div>
</template>

<style scoped>
.app-detail-v2 {
  min-height: 100vh;
  background:
    radial-gradient(circle at 18% 0%, rgba(80, 215, 255, 0.12), transparent 28rem),
    radial-gradient(circle at 84% 10%, rgba(69, 230, 138, 0.1), transparent 30rem),
    linear-gradient(180deg, #090b10 0%, #07080b 48%, #050608 100%);
  color: #f5f7fb;
  font-family: Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
}

.detail-page {
  padding: 42px 0 80px;
}

.detail-shell {
  width: min(1260px, calc(100% - 40px));
  margin: 0 auto;
}

.back-link {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 24px;
  color: #9ba3b4;
  font-size: 0.9rem;
  font-weight: 800;
}

.back-link:hover {
  color: #f5f7fb;
}

.detail-state {
  display: grid;
  place-items: center;
  gap: 14px;
  min-height: 360px;
  padding: 32px;
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 8px;
  background: rgba(255, 255, 255, 0.04);
  text-align: center;
  color: #9ba3b4;
}

.state-orb {
  width: 80px;
  height: 80px;
  border-radius: 999px;
  background:
    radial-gradient(circle at 30% 30%, rgba(255, 255, 255, 0.3), transparent 36%),
    linear-gradient(135deg, rgba(69, 230, 138, 0.32), rgba(80, 215, 255, 0.22));
}

.state-orb-error {
  background:
    radial-gradient(circle at 30% 30%, rgba(255, 255, 255, 0.24), transparent 36%),
    linear-gradient(135deg, rgba(255, 120, 120, 0.42), rgba(80, 215, 255, 0.18));
}

.detail-hero {
  display: grid;
  grid-template-columns: minmax(0, 0.74fr) minmax(680px, 1.26fr);
  gap: 34px;
  align-items: stretch;
}

.summary-panel {
  display: flex;
  flex-direction: column;
  justify-content: space-between;
  min-height: 520px;
  padding: 28px 0;
}

.detail-eyebrow {
  display: inline-flex;
  width: fit-content;
  padding: 6px 10px;
  border: 1px solid rgba(80, 215, 255, 0.22);
  border-radius: 999px;
  background: rgba(80, 215, 255, 0.08);
  color: #c4f2ff;
  font-size: 0.82rem;
  font-weight: 800;
}

.hero-title {
  margin: 18px 0 16px;
  color: #f5f7fb;
  font-size: clamp(3.1rem, 6.2vw, 6.1rem);
  line-height: 0.92;
  letter-spacing: -0.06em;
}

.developer-row {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 18px;
  color: #50d7ff;
  font-weight: 800;
}

.developer-link {
  color: #50d7ff;
}

.developer-link:hover {
  color: #9ee8ff;
}

.hero-description {
  max-width: 560px;
  margin: 0;
  color: #c8ceda;
  font-size: 1.12rem;
  line-height: 1.5;
  white-space: pre-line;
}

.hero-stats {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-top: 16px;
  color: #9fb0c2;
  font-size: 0.9rem;
  font-weight: 850;
}

.hero-stats span {
  display: inline-flex;
  align-items: center;
  gap: 8px;
}

.hero-stats span + span::before {
  content: "";
  width: 4px;
  height: 4px;
  border-radius: 999px;
  background: rgba(159, 176, 194, 0.68);
}

.hero-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
  margin-top: 28px;
}

.detail-button {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  min-height: 40px;
  padding: 0 16px;
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 8px;
  background: rgba(255, 255, 255, 0.04);
  color: #f5f7fb;
  font-weight: 800;
}

.detail-button.primary {
  border-color: rgba(69, 230, 138, 0.55);
  background: #45e68a;
  color: #041008;
}

.meta-strip {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 10px;
  margin-top: 34px;
}

.meta-card {
  padding: 14px;
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 8px;
  background: rgba(255, 255, 255, 0.04);
}

.meta-card strong {
  display: block;
  color: #f5f7fb;
  font-size: 1.08rem;
  line-height: 1.1;
}

.meta-card span {
  color: #aeb8c8;
  font-size: 0.86rem;
}

.detail-gallery {
  overflow: hidden;
  border: 1px solid rgba(255, 255, 255, 0.14);
  border-radius: 8px;
  background: rgba(15, 18, 24, 0.94);
  box-shadow: 0 24px 80px rgba(0, 0, 0, 0.36);
}

.gallery-stage {
  position: relative;
  overflow: hidden;
  display: grid;
  min-height: 520px;
  place-items: center;
  padding: 18px;
  background:
    radial-gradient(circle at 50% 36%, rgba(80, 215, 255, 0.08), transparent 28rem),
    #080b10;
}

.stage-image {
  width: min(100%, 780px);
  max-height: 480px;
  object-fit: contain;
  display: block;
  border-radius: 8px;
  box-shadow: 0 18px 60px rgba(0, 0, 0, 0.42);
}

.stage-fallback,
.related-fallback {
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 4rem;
  font-weight: 800;
  color: rgba(255, 255, 255, 0.9);
}

.gallery-strip {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(120px, 1fr));
  gap: 10px;
  padding: 14px;
  border-top: 1px solid rgba(255, 255, 255, 0.1);
  background: #172231;
}

.gallery-thumb {
  overflow: hidden;
  padding: 0;
  border: 1px solid rgba(255, 255, 255, 0.14);
  border-radius: 8px;
  background: #0b0f15;
  cursor: pointer;
  transition: transform 0.2s ease, border-color 0.2s ease;
}

.gallery-thumb:hover {
  transform: translateY(-2px);
  border-color: rgba(80, 215, 255, 0.36);
}

.gallery-thumb-active {
  border-color: rgba(80, 215, 255, 0.72);
  box-shadow: inset 0 0 0 1px rgba(80, 215, 255, 0.25);
}

.gallery-thumb img {
  width: 100%;
  aspect-ratio: 16 / 9;
  object-fit: cover;
  display: block;
  background: #0b0f15;
}

.gallery-arrow {
  position: absolute;
  top: 50%;
  z-index: 2;
  display: grid;
  width: 42px;
  height: 88px;
  place-items: center;
  transform: translateY(-50%);
  border: 1px solid rgba(255, 255, 255, 0.08);
  background: rgba(22, 36, 50, 0.78);
  color: #ffffff;
  cursor: pointer;
  font-size: 1.4rem;
}

.gallery-arrow-left {
  left: 0;
}

.gallery-arrow-right {
  right: 0;
}

.gallery-arrow:hover {
  color: #50d7ff;
}

.detail-body-grid {
  display: grid;
  grid-template-columns: minmax(0, 1fr) 340px;
  gap: 18px;
  margin-top: 28px;
}

.detail-card,
.related-card {
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 8px;
  background: rgba(255, 255, 255, 0.04);
}

.detail-card {
  padding: 22px;
}

.detail-card h2,
.detail-card h3 {
  margin: 0 0 10px;
  color: #f5f7fb;
  line-height: 1.05;
  letter-spacing: -0.035em;
}

.detail-card p {
  margin: 0;
  color: #9ba3b4;
}

.tag-row {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-top: 18px;
}

.tag {
  display: inline-flex;
  padding: 7px 10px;
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 999px;
  background: rgba(255, 255, 255, 0.03);
  color: #9ba3b4;
  font-size: 0.82rem;
  font-weight: 800;
}

.fork-card {
  display: grid;
  align-content: start;
  gap: 14px;
}

.fork-button {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  min-height: 42px;
  border: 1px solid rgba(69, 230, 138, 0.45);
  border-radius: 8px;
  background: rgba(69, 230, 138, 0.14);
  color: #45e68a;
  font-weight: 900;
}

.fork-button svg {
  width: 16px;
  height: 16px;
}

.related-section {
  margin-top: 36px;
}

.related-header {
  margin-bottom: 18px;
}

.related-title {
  margin: 0;
  color: #f5f7fb;
  font-size: clamp(2rem, 4vw, 3.4rem);
  line-height: 1;
  letter-spacing: -0.045em;
}

.related-grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 14px;
}

.related-card {
  overflow: hidden;
  transition: transform 0.22s ease, border-color 0.22s ease;
}

.related-card:hover {
  transform: translateY(-4px);
  border-color: rgba(80, 215, 255, 0.28);
}

.related-media {
  aspect-ratio: 16 / 10;
  background: #080b10;
}

.related-image {
  width: 100%;
  height: 100%;
  object-fit: cover;
}

.related-body {
  display: grid;
  padding: 16px;
}

.related-name {
  margin: 0 0 6px;
  color: #f5f7fb;
  font-size: 1.02rem;
  line-height: 1.2;
}

.related-summary {
  margin: 0;
  color: #9ba3b4;
  font-size: 0.9rem;
  display: -webkit-box;
  -webkit-line-clamp: 3;
  -webkit-box-orient: vertical;
  overflow: hidden;
}

.related-creator {
  margin-bottom: 8px;
  color: #50d7ff;
  font-size: 0.82rem;
  font-weight: 700;
}

@media (max-width: 1060px) {
  .detail-hero,
  .detail-body-grid,
  .related-grid {
    grid-template-columns: 1fr;
  }

  .summary-panel {
    min-height: auto;
    padding: 0;
  }

  .gallery-stage {
    min-height: 360px;
  }
}

@media (max-width: 720px) {
  .detail-page {
    padding: 26px 0 84px;
  }

  .detail-shell {
    width: min(100% - 28px, 1260px);
  }

  .hero-title {
    font-size: clamp(2.9rem, 15vw, 4.4rem);
  }

  .meta-strip,
  .gallery-strip {
    grid-template-columns: repeat(2, 1fr);
  }

  .gallery-stage {
    min-height: 280px;
  }

  .stage-image {
    max-height: 260px;
  }

  .gallery-arrow {
    width: 38px;
    height: 72px;
  }
}
</style>
