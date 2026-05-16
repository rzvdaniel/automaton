<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import AppFooter from '@/components/AppFooter.vue'
import AppNav from '@/components/AppNav.vue'
import { publicAppConfig } from '@/config/publicAppConfig'
import type { MarketplaceAppDetails } from '@/types/marketplace'

const route = useRoute()
const marketplaceApiBaseUrl = `${publicAppConfig.apiBaseUrl}api/public/marketplace`

const app = ref<MarketplaceAppDetails | null>(null)
const loading = ref(true)
const selectedImageUrl = ref('')
const errorMessage = ref('')

function resolveAssetUrl(url?: string | null): string {
  if (!url) {
    return ''
  }

  if (url.startsWith('http')) {
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

async function loadApp() {
  loading.value = true
  errorMessage.value = ''

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
  } catch {
    errorMessage.value = 'Unable to load this app right now.'
    app.value = null
    selectedImageUrl.value = ''
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
  <div>
    <AppNav />
    <main class="detail-page">
      <div class="container">
        <RouterLink to="/apps" class="back-link">
          <i class="pi pi-arrow-left" />
          Back to marketplace
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
          <section class="detail-panel detail-summary-card">
            <div class="hero-copy">
              <div class="hero-badges">
                <span v-if="app.category" class="tag">{{ app.category }}</span>
                <span v-if="app.isFeatured" class="hero-chip">Featured</span>
              </div>

              <h1 class="hero-title">{{ app.name }}</h1>
              <div v-if="app.creatorPublicUsername" class="developer-row">
                <span>Developer:</span>
                <RouterLink :to="`/${app.creatorPublicUsername}`" class="developer-link">
                  {{ app.creatorDisplayName || app.creatorPublicUsername }}
                </RouterLink>
              </div>

              <div class="hero-stats">
                <span class="hero-stat">⭐ {{ app.starCount }} stars</span>
                <span class="hero-stat">👁 {{ app.viewCount }} views</span>
                <span v-if="publishedLabel" class="hero-stat">Published {{ publishedLabel }}</span>
              </div>

              <p v-if="app.description" class="hero-description">{{ app.description }}</p>

              <div v-if="tagList.length" class="tag-row">
                <span v-for="tag in tagList" :key="tag" class="tag">{{ tag }}</span>
              </div>

              <div class="hero-actions">
                <a v-if="launchUrl" :href="launchUrl" target="_blank" rel="noopener" class="btn btn-primary">
                  Open app
                  <i class="pi pi-external-link" />
                </a>
                <RouterLink to="/apps" class="btn btn-secondary">Explore more apps</RouterLink>
              </div>
            </div>
          </section>

          <section class="detail-panel media-panel">
            <div class="hero-gallery">
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
            </div>
          </section>

          <section v-if="app.relatedApps.length" class="related-section">
            <div class="related-header">
              <div>
                <span class="section-label">More like this</span>
                <h2 class="section-title related-title">Keep exploring this category</h2>
              </div>
            </div>

            <div class="related-grid">
              <RouterLink v-for="relatedApp in app.relatedApps" :key="relatedApp.id" :to="`/apps/${relatedApp.id}`" class="related-card">
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
                  <div class="related-meta">
                    <span v-if="relatedApp.category" class="tag">{{ relatedApp.category }}</span>
                    <span class="related-metric">⭐ {{ relatedApp.starCount }}</span>
                  </div>
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
.detail-page {
  padding: 40px 0 100px;
}

.back-link {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 24px;
  color: var(--color-text-muted);
  font-weight: 600;
}

.back-link:hover {
  color: var(--color-text);
}

.detail-state {
  display: grid;
  place-items: center;
  gap: 14px;
  min-height: 360px;
  padding: 32px;
  border-radius: 28px;
  border: 1px solid var(--color-border);
  background:
    radial-gradient(circle at top, rgba(124, 106, 245, 0.14), transparent 40%),
    var(--color-surface);
  text-align: center;
  color: var(--color-text-muted);
}

.state-orb {
  width: 80px;
  height: 80px;
  border-radius: 999px;
  background:
    radial-gradient(circle at 30% 30%, rgba(255, 255, 255, 0.3), transparent 36%),
    linear-gradient(135deg, rgba(124, 106, 245, 0.42), rgba(77, 166, 255, 0.22));
}

.state-orb-error {
  background:
    radial-gradient(circle at 30% 30%, rgba(255, 255, 255, 0.24), transparent 36%),
    linear-gradient(135deg, rgba(255, 120, 120, 0.42), rgba(124, 106, 245, 0.18));
}

.detail-panel,
.hero-copy,
.hero-gallery,
.related-card {
  border: 1px solid rgba(255, 255, 255, 0.08);
  background:
    radial-gradient(circle at top right, rgba(77, 166, 255, 0.12), transparent 28%),
    linear-gradient(180deg, rgba(255, 255, 255, 0.04), rgba(255, 255, 255, 0.02)),
    var(--color-surface);
  box-shadow: 0 24px 80px rgba(0, 0, 0, 0.28);
}

.detail-panel {
  border-radius: 30px;
}

.detail-summary-card {
  overflow: hidden;
  background:
    radial-gradient(circle at top right, rgba(77, 166, 255, 0.18), transparent 34%),
    radial-gradient(circle at bottom left, rgba(124, 106, 245, 0.1), transparent 34%),
    linear-gradient(180deg, rgba(255, 255, 255, 0.05), rgba(255, 255, 255, 0.02)),
    var(--color-surface);
}

.hero-copy {
  display: grid;
  gap: 14px;
  padding: 24px 28px;
  border: 0;
  border-radius: 0;
  background: transparent;
  box-shadow: none;
}

.hero-badges,
.hero-stats,
.tag-row,
.hero-actions,
.related-meta {
  display: flex;
  gap: 8px;
  flex-wrap: wrap;
}

.hero-actions {
  align-items: stretch;
}

.hero-chip,
.hero-stat {
  display: inline-flex;
  align-items: center;
  padding: 6px 10px;
  border-radius: 999px;
  background: rgba(255, 255, 255, 0.05);
  color: var(--color-text-muted);
  font-size: 0.8rem;
  font-weight: 600;
}

.hero-chip {
  color: #fff;
  background: linear-gradient(135deg, rgba(124, 106, 245, 0.34), rgba(77, 166, 255, 0.18));
}

.hero-title {
  font-size: clamp(1.75rem, 3.4vw, 2.55rem);
  line-height: 1.05;
  letter-spacing: -0.03em;
}

.developer-row {
  display: flex;
  align-items: center;
  gap: 8px;
  color: var(--color-text-muted);
  font-size: 0.9rem;
  font-weight: 700;
}

.developer-link {
  color: var(--color-blue);
}

.developer-link:hover {
  color: #7cc3ff;
}

.hero-description {
  color: var(--color-text-muted);
  font-size: 0.95rem;
  line-height: 1.45;
  white-space: pre-line;
  max-width: 56rem;
}

.media-panel {
  margin-top: 22px;
  padding: 18px;
}

.hero-gallery {
  display: grid;
  gap: 16px;
  padding: 0;
  border: 0;
  border-radius: 0;
  background: transparent;
  box-shadow: none;
}

.gallery-stage {
  position: relative;
  overflow: hidden;
  height: min(62vw, 560px);
  min-height: 360px;
  border-radius: 18px;
  background: #05070d;
}

.stage-image {
  width: 100%;
  height: 100%;
  min-height: 360px;
  object-fit: contain;
  display: block;
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
  display: flex;
  gap: 10px;
  overflow-x: auto;
  padding-bottom: 4px;
  scrollbar-width: thin;
}

.gallery-thumb {
  flex: 0 0 148px;
  overflow: hidden;
  padding: 0;
  border: 1px solid rgba(255, 255, 255, 0.08);
  border-radius: 8px;
  background: var(--color-surface-2);
  cursor: pointer;
  transition: transform 0.2s ease, border-color 0.2s ease;
}

.gallery-thumb:hover {
  transform: translateY(-2px);
  border-color: rgba(124, 106, 245, 0.34);
}

.gallery-thumb-active {
  border-color: rgba(124, 106, 245, 0.52);
  box-shadow: inset 0 0 0 1px rgba(124, 106, 245, 0.25);
}

.gallery-thumb img {
  width: 100%;
  aspect-ratio: 16 / 9;
  object-fit: contain;
  display: block;
  background: #060910;
}

.gallery-arrow {
  position: absolute;
  top: 50%;
  z-index: 2;
  width: 54px;
  height: 112px;
  transform: translateY(-50%);
  border: 0;
  background: linear-gradient(90deg, rgba(103, 193, 245, 0.28), rgba(103, 193, 245, 0));
  color: #ffffff;
  cursor: pointer;
  font-size: 1.8rem;
}

.gallery-arrow-left {
  left: 0;
}

.gallery-arrow-right {
  right: 0;
  background: linear-gradient(270deg, rgba(103, 193, 245, 0.28), rgba(103, 193, 245, 0));
}

.gallery-arrow:hover {
  color: #66c0f4;
}

.related-section {
  margin-top: 42px;
}

.related-header {
  margin-bottom: 20px;
}

.related-title {
  font-size: clamp(1.6rem, 4vw, 2.4rem);
}

.related-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(240px, 1fr));
  gap: 20px;
}

.related-card {
  display: grid;
  overflow: hidden;
  border-radius: 24px;
  transition: transform 0.22s ease, border-color 0.22s ease;
}

.related-card:hover {
  transform: translateY(-4px);
  border-color: rgba(124, 106, 245, 0.3);
}

.related-media {
  aspect-ratio: 16 / 10;
  background: linear-gradient(135deg, rgba(124, 106, 245, 0.22), rgba(77, 166, 255, 0.14));
}

.related-image {
  width: 100%;
  height: 100%;
  object-fit: cover;
}

.related-body {
  display: grid;
  gap: 12px;
  padding: 16px;
}

.related-name {
  font-size: 1.02rem;
  line-height: 1.2;
}

.related-summary {
  color: var(--color-text-muted);
  font-size: 0.9rem;
  display: -webkit-box;
  -webkit-line-clamp: 3;
  -webkit-box-orient: vertical;
  overflow: hidden;
}

.related-creator {
  color: var(--color-blue);
  font-size: 0.82rem;
  font-weight: 700;
}

.related-metric {
  color: var(--color-text-muted);
  font-size: 0.8rem;
  font-weight: 700;
}

@media (max-width: 720px) {
  .detail-page {
    padding: 28px 0 84px;
  }

  .hero-copy {
    padding: 18px;
    border-radius: 24px;
  }

  .media-panel {
    padding: 12px;
  }

  .gallery-stage,
  .stage-image {
    min-height: 280px;
  }

  .gallery-stage {
    height: 320px;
  }

  .gallery-thumb {
    flex-basis: 112px;
  }
}
</style>
