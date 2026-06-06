<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import AppFooter from '@/components/AppFooter.vue'
import AppsDiscoveryStrip from '@/components/AppsDiscoveryStrip.vue'
import PublicSiteHeader from '@/components/PublicSiteHeader.vue'
import { getPublishedAppUrl, publicAppConfig } from '@/config/publicAppConfig'
import type { MarketplaceAppCard, MarketplaceAppDetails } from '@/types/marketplace'

const route = useRoute()
const marketplaceApiBaseUrl = `${publicAppConfig.apiBaseUrl}api/public/marketplace`

const app = ref<MarketplaceAppDetails | null>(null)
const creatorApps = ref<MarketplaceAppCard[]>([])
const loading = ref(true)
const selectedImageUrl = ref('')
const errorMessage = ref('')
const shareMessage = ref('')

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
  app.value?.publicUrl ? getPublishedAppUrl(app.value.publicUrl) : null,
)

const forkUrl = computed(() =>
  app.value ? `${publicAppConfig.studioUrl}apps/fork/${app.value.id}` : publicAppConfig.studioUrl,
)

const creatorName = computed(() =>
  app.value?.creatorDisplayName || app.value?.creatorPublicUsername || 'Automaton creator',
)

const creatorInitials = computed(() =>
  creatorName.value
    .split(/\s+/)
    .map((part) => part.charAt(0))
    .join('')
    .slice(0, 2)
    .toUpperCase(),
)

const heroSummary = computed(() =>
  app.value?.summary || app.value?.description || 'Open the app to see what this public Automaton build does.',
)

const aboutDescription = computed(() =>
  app.value?.description || app.value?.summary || 'Open the app to see what this public Automaton build does.',
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

const updatedLabel = computed(() => {
  const source = app.value?.updatedAt || app.value?.publishedAt
  if (!source) return 'updated recently'

  const updated = new Date(source)
  if (Number.isNaN(updated.getTime())) return 'updated recently'

  const diffMs = Date.now() - updated.getTime()
  const diffMinutes = Math.max(0, Math.floor(diffMs / 60000))
  const diffHours = Math.floor(diffMinutes / 60)
  const diffDays = Math.floor(diffHours / 24)

  if (diffMinutes < 60) return 'updated today'
  if (diffHours < 24) return 'updated today'
  if (diffDays === 1) return 'updated yesterday'
  if (diffDays < 7) return `updated ${diffDays}d ago`
  if (diffDays < 30) return `updated ${Math.floor(diffDays / 7)}w ago`
  return `updated ${Math.floor(diffDays / 30)}mo ago`
})

const relatedHeading = computed(() => {
  const name = app.value?.creatorDisplayName || app.value?.creatorPublicUsername
  return name ? `More apps from ${name}` : 'More apps from this author'
})

function formatCount(value: number) {
  if (value >= 1000) return `${(value / 1000).toFixed(value >= 10000 ? 0 : 1)}k`
  return String(value)
}

function pluralize(value: number, singular: string, plural = `${singular}s`) {
  return `${formatCount(value)} ${value === 1 ? singular : plural}`
}

function relatedImageUrl(relatedApp: MarketplaceAppCard) {
  return relatedApp.thumbnailUrl || relatedApp.screenshots?.[0]?.imageUrl || ''
}

async function shareApp() {
  shareMessage.value = ''

  const title = app.value?.name || 'Automaton app'
  const url = launchUrl.value || window.location.href

  try {
    if (navigator.share) {
      await navigator.share({ title, url })
      return
    }

    await navigator.clipboard.writeText(url)
    shareMessage.value = 'Link ready on your clipboard.'
  } catch {
    shareMessage.value = 'Could not share this app.'
  }
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
            <aside class="summary-panel">
              <div class="badge-row">
                <span class="badge">Live app</span>
                <span v-if="app.isFeatured" class="badge badge-hot">Featured</span>
                <span v-if="app.canRemix" class="badge badge-forkable">Forkable</span>
                <span class="badge badge-static">{{ app.hasBackendExecution ? 'Backend' : 'Static' }}</span>
              </div>

              <div class="summary-main">
                <h1 class="hero-title">{{ app.name }}</h1>
                <div v-if="app.creatorPublicUsername" class="developer-row">
                  <RouterLink :to="`/creators/${app.creatorPublicUsername}`" class="developer-link">
                    <span class="creator-avatar">{{ creatorInitials }}</span>
                    <span>by {{ creatorName }}</span>
                  </RouterLink>
                </div>
                <div v-else class="developer-row developer-row-static">
                  <span class="creator-avatar">{{ creatorInitials }}</span>
                  <span>by {{ creatorName }}</span>
                </div>

                <p class="hero-description">{{ heroSummary }}</p>

                <div class="hero-stats" aria-label="App activity">
                  <span><i class="pi pi-play" /> {{ pluralize(app.viewCount, 'play') }}</span>
                  <span v-if="app.canRemix">
                    <svg viewBox="0 0 16 16" aria-hidden="true" focusable="false">
                      <path d="M5 3.25a2.25 2.25 0 1 0-1.5 2.122v5.256a2.251 2.251 0 1 0 1.5 0V9.372a2.251 2.251 0 0 0 1.746-1.554h2.628a2.251 2.251 0 1 0 0-1.5H6.746A2.251 2.251 0 0 0 5 4.628V3.25Z" fill="currentColor" />
                    </svg>
                    {{ pluralize(app.forkCount, 'fork') }}
                  </span>
                  <span>{{ updatedLabel }}</span>
                </div>

                <div class="hero-actions">
                  <a v-if="launchUrl" :href="launchUrl" target="_blank" rel="noopener" class="detail-button primary">
                    Open app
                    <i class="pi pi-external-link" />
                  </a>
                  <a v-if="app.canRemix" :href="forkUrl" class="detail-button fork-action" target="_blank" rel="noopener">
                    <svg viewBox="0 0 16 16" aria-hidden="true" focusable="false">
                      <path d="M5 3.25a2.25 2.25 0 1 0-1.5 2.122v5.256a2.251 2.251 0 1 0 1.5 0V9.372a2.251 2.251 0 0 0 1.746-1.554h2.628a2.251 2.251 0 1 0 0-1.5H6.746A2.251 2.251 0 0 0 5 4.628V3.25Z" fill="currentColor" />
                    </svg>
                    Fork app
                  </a>
                  <button class="detail-button" type="button" @click="shareApp">
                    Share
                    <i class="pi pi-share-alt" />
                  </button>
                </div>
                <p v-if="shareMessage" class="share-message">{{ shareMessage }}</p>

                <div class="hero-pills" aria-label="App metadata">
                  <span v-if="app.category" class="hero-pill">{{ app.category }}</span>
                  <span class="hero-pill">{{ app.hasBackendExecution ? 'Backend app' : 'Static app' }}</span>
                  <span class="hero-pill">{{ app.hasBackendExecution ? 'Runner required' : 'Runner not required' }}</span>
                  <span v-if="app.canRemix" class="hero-pill hero-pill-forkable">Free to fork</span>
                  <span v-if="publishedLabel" class="hero-pill">published {{ publishedLabel }}</span>
                </div>
              </div>
            </aside>

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

          <nav class="detail-tabs" aria-label="App detail sections">
            <a href="#overview" class="detail-tab detail-tab-active">Overview</a>
            <a href="#forks" class="detail-tab">Forks & Remixes</a>
            <a href="#runtime" class="detail-tab">Runtime</a>
            <a href="#creator" class="detail-tab">Creator</a>
          </nav>

          <section id="overview" class="detail-body-grid">
            <article class="detail-card detail-card-main">
              <h2>About this app</h2>
              <p>{{ aboutDescription }}</p>
              <div v-if="tagList.length" class="tag-row">
                <span v-for="tag in tagList" :key="tag" class="tag">{{ tag }}</span>
              </div>

              <section id="forks" class="remix-section">
                <div class="section-heading-row">
                  <div>
                    <h3>Forks & Remixes</h3>
                    <p>Forks preserve attribution and show how apps evolve over time.</p>
                  </div>
                  <span v-if="app.canRemix" class="remix-count">{{ pluralize(app.forkCount, 'fork') }}</span>
                </div>

                <div v-if="app.forksAndRemixes?.length" class="remix-grid">
                  <RouterLink
                    v-for="remix in app.forksAndRemixes"
                    :key="remix.id"
                    :to="`/apps/${remix.id}`"
                    class="remix-card"
                  >
                    <img
                      v-if="relatedImageUrl(remix)"
                      :src="resolveAssetUrl(relatedImageUrl(remix))"
                      :alt="remix.name"
                    />
                    <div v-else class="remix-image-fallback">{{ remix.name.charAt(0) }}</div>
                    <div>
                      <strong>{{ remix.name }}</strong>
                      <span>by {{ remix.creatorDisplayName || remix.creatorPublicUsername || 'Automaton creator' }}</span>
                    </div>
                  </RouterLink>
                </div>

                <p v-else-if="app.canRemix" class="empty-copy">
                  No public remixes yet. Fork this app to create a private editable copy, modify it, then publish your version when ready.
                </p>
                <p v-else class="empty-copy">This app is published for use. Remixing is not currently available for this app.</p>
              </section>
            </article>

            <aside id="runtime" class="side-stack">
              <section v-if="app.canRemix" class="detail-card fork-panel">
                <h3>Fork this app</h3>
                <p>Create your own private editable copy, modify it with your AI coding tools, then publish your version when ready.</p>
                <a :href="forkUrl" class="detail-button fork-action side-action" target="_blank" rel="noopener">
                  <svg viewBox="0 0 16 16" aria-hidden="true" focusable="false">
                    <path d="M5 3.25a2.25 2.25 0 1 0-1.5 2.122v5.256a2.251 2.251 0 1 0 1.5 0V9.372a2.251 2.251 0 0 0 1.746-1.554h2.628a2.251 2.251 0 1 0 0-1.5H6.746A2.251 2.251 0 0 0 5 4.628V3.25Z" fill="currentColor" />
                  </svg>
                  Fork app
                </a>
              </section>

              <section class="detail-card">
                <h3>Runtime</h3>
                <div class="runtime-list">
                  <div class="runtime-item"><span>Frontend</span><span>Static hosting</span></div>
                  <div class="runtime-item">
                    <span>Backend</span>
                    <span>{{ app.hasBackendExecution ? 'Requires Runner' : 'Not required' }}</span>
                  </div>
                  <div class="runtime-item">
                    <span>Fork & publish</span>
                    <span>{{ app.hasBackendExecution ? 'Runner needed' : 'Free' }}</span>
                  </div>
                </div>
              </section>
            </aside>
          </section>

          <section v-if="creatorApps.length" id="creator" class="related-section">
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
  grid-template-columns: minmax(360px, 0.92fr) minmax(0, 1.08fr);
  gap: 28px;
  align-items: start;
}

.summary-panel {
  position: sticky;
  top: 144px;
  display: grid;
  align-content: start;
  gap: 18px;
  padding-top: 6px;
}

.badge-row {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.badge {
  padding: 5px 9px;
  border-radius: 999px;
  background: rgba(80, 215, 255, 0.12);
  color: #bdefff;
  font-size: 0.72rem;
  font-weight: 950;
  letter-spacing: 0.04em;
  text-transform: uppercase;
}

.badge-static {
  background: rgba(255, 255, 255, 0.08);
  color: #d6dbe7;
}

.badge-forkable {
  background: rgba(69, 230, 138, 0.14);
  color: #45e68a;
}

.badge-hot {
  background: rgba(255, 203, 87, 0.13);
  color: #ffcb57;
}

.hero-title {
  margin: 0;
  color: #f5f7fb;
  font-size: clamp(3rem, 5.8vw, 5.8rem);
  line-height: 0.94;
  letter-spacing: -0.06em;
}

.summary-main {
  display: grid;
  gap: 16px;
}

.developer-row {
  display: inline-flex;
  align-items: center;
  width: fit-content;
  gap: 9px;
  color: #50d7ff;
  font-weight: 850;
}

.developer-link {
  display: inline-flex;
  align-items: center;
  gap: 9px;
  color: #50d7ff;
  cursor: pointer;
}

.developer-link:hover {
  color: #9ee8ff;
  text-decoration: underline;
  text-underline-offset: 0.18em;
}

.developer-row-static {
  color: #9ba3b4;
}

.creator-avatar {
  display: grid;
  width: 27px;
  height: 27px;
  place-items: center;
  border-radius: 999px;
  background: linear-gradient(135deg, #45e68a, #50d7ff);
  color: #031008;
  font-size: 0.72rem;
  font-weight: 950;
}

.hero-description {
  max-width: 560px;
  margin: 0;
  color: #c8ceda;
  font-size: 1.02rem;
  line-height: 1.5;
  white-space: pre-line;
}

.hero-stats {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
  color: #9fb0c2;
  font-size: 0.86rem;
  font-weight: 850;
}

.hero-stats span {
  display: inline-flex;
  align-items: center;
  gap: 5px;
}

.hero-stats i,
.hero-stats svg {
  width: 0.82rem;
  height: 0.82rem;
  font-size: 0.78rem;
}

.hero-actions {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, auto));
  justify-content: start;
  gap: 10px;
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
  cursor: pointer;
  transition:
    transform 0.18s ease,
    border-color 0.18s ease,
    background 0.18s ease,
    color 0.18s ease;
}

.detail-button.primary {
  border-color: rgba(69, 230, 138, 0.55);
  background: #45e68a;
  color: #041008;
  box-shadow: 0 16px 44px rgba(69, 230, 138, 0.16);
}

.detail-button:hover {
  transform: translateY(-1px);
  border-color: rgba(255, 255, 255, 0.2);
  background: rgba(255, 255, 255, 0.065);
}

.detail-button.primary:hover {
  border-color: rgba(69, 230, 138, 0.72);
  background: #6cf0a3;
  color: #041008;
}

.fork-action {
  border-color: rgba(69, 230, 138, 0.38);
  background: rgba(69, 230, 138, 0.14);
  color: #45e68a;
}

.fork-action:hover {
  border-color: rgba(69, 230, 138, 0.5);
  background: rgba(69, 230, 138, 0.18);
  color: #8affb7;
}

.fork-action svg {
  width: 16px;
  height: 16px;
}

.share-message {
  margin: -4px 0 0;
  color: #9ba3b4;
  font-size: 0.84rem;
  font-weight: 750;
}

.hero-pills {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.hero-pill {
  display: inline-flex;
  align-items: center;
  min-height: 30px;
  padding: 0 10px;
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 999px;
  background: rgba(255, 255, 255, 0.04);
  color: #aeb8c8;
  font-size: 0.82rem;
  font-weight: 800;
}

.hero-pill-forkable {
  border-color: rgba(69, 230, 138, 0.24);
  background: rgba(69, 230, 138, 0.1);
  color: #bdfbd2;
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
  aspect-ratio: 16 / 9;
  place-items: center;
  padding: 18px;
  background:
    radial-gradient(circle at 50% 36%, rgba(80, 215, 255, 0.08), transparent 28rem),
    #080b10;
}

.stage-image {
  width: 100%;
  height: 100%;
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
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 10px;
  padding: 14px;
  border-top: 1px solid rgba(255, 255, 255, 0.1);
  background: #172231;
}

.gallery-thumb {
  overflow: hidden;
  aspect-ratio: 16 / 9;
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
  grid-template-columns: minmax(0, 1fr) 360px;
  gap: 18px;
  margin-top: 18px;
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

.detail-card p + p {
  margin-top: 12px;
}

.detail-tabs {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-top: 28px;
  padding: 8px;
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 8px;
  background: rgba(255, 255, 255, 0.035);
}

.detail-tab {
  padding: 9px 13px;
  border-radius: 7px;
  color: #9ba3b4;
  font-size: 0.86rem;
  font-weight: 850;
  cursor: pointer;
}

.detail-tab:hover {
  color: #f5f7fb;
  background: rgba(255, 255, 255, 0.055);
}

.detail-tab-active {
  background: rgba(69, 230, 138, 0.14);
  color: #45e68a;
}

.remix-section {
  margin-top: 24px;
  padding-top: 22px;
  border-top: 1px solid rgba(255, 255, 255, 0.1);
}

.section-heading-row {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 14px;
}

.section-heading-row p {
  margin-top: 0;
}

.remix-count {
  flex: 0 0 auto;
  padding: 6px 10px;
  border: 1px solid rgba(69, 230, 138, 0.24);
  border-radius: 999px;
  background: rgba(69, 230, 138, 0.1);
  color: #bdfbd2;
  font-size: 0.82rem;
  font-weight: 850;
}

.remix-grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 14px;
  margin-top: 14px;
}

.remix-card {
  overflow: hidden;
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 8px;
  background: rgba(0, 0, 0, 0.14);
  cursor: pointer;
  transition:
    transform 0.2s ease,
    border-color 0.2s ease,
    background 0.2s ease;
}

.remix-card:hover {
  transform: translateY(-2px);
  border-color: rgba(69, 230, 138, 0.32);
  background: rgba(255, 255, 255, 0.045);
}

.remix-card img,
.remix-image-fallback {
  width: 100%;
  height: 118px;
  object-fit: cover;
  background: #080b10;
}

.remix-image-fallback {
  display: grid;
  place-items: center;
  color: #f5f7fb;
  font-size: 2.6rem;
  font-weight: 900;
}

.remix-card div {
  padding: 12px;
}

.remix-card strong {
  display: block;
  margin-bottom: 4px;
  color: #f5f7fb;
  font-size: 0.95rem;
}

.remix-card span {
  color: #9ba3b4;
  font-size: 0.78rem;
  font-weight: 850;
}

.empty-copy {
  margin-top: 14px !important;
}

.side-stack {
  display: grid;
  gap: 18px;
  align-content: start;
}

.fork-panel {
  border-color: rgba(69, 230, 138, 0.24);
  background:
    linear-gradient(135deg, rgba(69, 230, 138, 0.07), rgba(80, 215, 255, 0.035)),
    rgba(255, 255, 255, 0.04);
}

.side-action {
  width: 100%;
  margin-top: 16px;
}

.runtime-list {
  display: grid;
  gap: 10px;
  margin-top: 14px;
}

.runtime-item {
  display: flex;
  justify-content: space-between;
  gap: 12px;
  padding: 12px;
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 8px;
  background: rgba(0, 0, 0, 0.14);
  color: #c8ceda;
  font-weight: 850;
}

.runtime-item span:last-child {
  color: #9ba3b4;
  font-size: 0.84rem;
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
  cursor: pointer;
  transition: transform 0.22s ease, border-color 0.22s ease;
}

.related-card:hover {
  transform: translateY(-2px);
  border-color: rgba(69, 230, 138, 0.32);
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
    position: static;
    padding-top: 0;
  }

  .gallery-stage {
    aspect-ratio: 16 / 9;
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
    font-size: clamp(2.8rem, 14vw, 4.2rem);
  }

  .hero-actions {
    grid-template-columns: 1fr;
  }

  .gallery-strip,
  .remix-grid {
    grid-template-columns: repeat(2, 1fr);
  }

  .gallery-stage {
    aspect-ratio: 16 / 9;
  }

  .gallery-arrow {
    width: 38px;
    height: 72px;
  }
}
</style>
