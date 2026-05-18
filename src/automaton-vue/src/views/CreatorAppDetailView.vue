<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import AppFooter from '@/components/AppFooter.vue'
import AppsDiscoveryStrip from '@/components/AppsDiscoveryStrip.vue'
import PublicSiteHeader from '@/components/PublicSiteHeader.vue'
import { publicAppConfig } from '@/config/publicAppConfig'
import type { MarketplaceAppDetails, PublicUserProfile } from '@/types/marketplace'

const route = useRoute()

const app = ref<MarketplaceAppDetails | null>(null)
const profile = ref<PublicUserProfile | null>(null)
const loading = ref(true)
const errorMessage = ref('')
const selectedImageUrl = ref('')

const publicUsername = computed(() => String(route.params.publicUsername || ''))
const appId = computed(() => String(route.params.appId || ''))
const creatorName = computed(() => profile.value?.displayName || profile.value?.username || publicUsername.value)

function resolveAssetUrl(url?: string | null): string {
  if (!url) return ''
  if (url.startsWith('http')) return url
  return `${publicAppConfig.apiBaseUrl}${url.replace(/^\/+/, '')}`
}

const galleryImages = computed(() => {
  if (!app.value) return []

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
    ...(app.value.screenshots ?? []).map((screenshot) => ({
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
  if (!app.value?.publishedAt) return null

  return new Date(app.value.publishedAt).toLocaleDateString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
  })
})

async function loadPage() {
  loading.value = true
  errorMessage.value = ''

  try {
    const [profileResponse, appResponse] = await Promise.all([
      fetch(`${publicAppConfig.apiBaseUrl}api/public/${encodeURIComponent(publicUsername.value)}/profile`),
      fetch(
        `${publicAppConfig.apiBaseUrl}api/public/${encodeURIComponent(publicUsername.value)}/apps/${appId.value}`,
      ),
    ])

    if (!profileResponse.ok || !appResponse.ok) {
      errorMessage.value = appResponse.status === 404 ? 'This app is no longer available.' : 'Unable to load this app.'
      profile.value = null
      app.value = null
      selectedImageUrl.value = ''
      return
    }

    profile.value = (await profileResponse.json()) as PublicUserProfile
    app.value = (await appResponse.json()) as MarketplaceAppDetails
    selectedImageUrl.value = galleryImages.value[0]?.imageUrl || ''
  } catch {
    errorMessage.value = 'Unable to load this app.'
    profile.value = null
    app.value = null
    selectedImageUrl.value = ''
  } finally {
    loading.value = false
  }
}

watch(() => route.fullPath, loadPage)
onMounted(loadPage)
</script>

<template>
  <div>
    <PublicSiteHeader />
    <AppsDiscoveryStrip />
    <main class="creator-detail-page">
      <div class="container">
        <RouterLink :to="`/${publicUsername}`" class="back-link">
          <i class="pi pi-arrow-left"></i>
          Back to {{ creatorName }}
        </RouterLink>

        <section v-if="loading" class="detail-state">
          <div class="state-orb"></div>
          <p>Loading app details...</p>
        </section>

        <section v-else-if="errorMessage" class="detail-state">
          <div class="state-orb"></div>
          <p>{{ errorMessage }}</p>
          <RouterLink :to="`/${publicUsername}`" class="btn btn-secondary">Back to creator store</RouterLink>
        </section>

        <template v-else-if="app">
          <section class="product-shell">
            <div class="product-heading">
              <h1>{{ app.name }}</h1>
              <span v-if="app.isFeatured" class="hero-chip">Featured</span>
            </div>

            <div class="hero-media">
              <img
                v-if="selectedImageUrl"
                :src="resolveAssetUrl(selectedImageUrl)"
                :alt="app.name"
                class="hero-image"
              />
              <div v-else class="hero-fallback">{{ app.name.slice(0, 1) }}</div>
            </div>

            <aside class="hero-copy">
              <div>
                <div class="creator-label">Built by</div>
                <RouterLink :to="`/${publicUsername}`" class="creator-link">{{ creatorName }}</RouterLink>
              </div>

              <div class="meta-row">
                <span v-if="app.category" class="meta-pill">{{ app.category }}</span>
                <span class="meta-stat"><i class="pi pi-star-fill"></i>{{ app.starCount }} stars</span>
                <span class="meta-stat"><i class="pi pi-eye"></i>{{ app.viewCount }} views</span>
                <span v-if="publishedLabel" class="meta-stat">Published {{ publishedLabel }}</span>
              </div>

              <p v-if="app.description || app.summary" class="app-description">
                {{ app.description || app.summary }}
              </p>

              <div v-if="tagList.length" class="tag-row">
                <span v-for="tag in tagList" :key="tag" class="tag">{{ tag }}</span>
              </div>

              <div class="hero-actions">
                <a v-if="launchUrl" :href="launchUrl" target="_blank" rel="noopener" class="btn btn-primary">
                  Open app
                  <i class="pi pi-external-link"></i>
                </a>
                <RouterLink :to="`/${publicUsername}`" class="btn btn-secondary">More by {{ creatorName }}</RouterLink>
              </div>
            </aside>
          </section>

          <section v-if="galleryImages.length > 1" class="gallery-section">
            <span class="section-label">Gallery</span>
            <div class="gallery-grid">
              <button
                v-for="image in galleryImages"
                :key="image.id"
                type="button"
                class="gallery-card"
                :class="{ active: selectedImageUrl === image.imageUrl }"
                @click="selectedImageUrl = image.imageUrl"
              >
                <img :src="resolveAssetUrl(image.imageUrl)" :alt="image.caption" />
                <span>{{ image.caption }}</span>
              </button>
            </div>
          </section>
        </template>
      </div>
    </main>
    <AppFooter />
  </div>
</template>

<style scoped>
.creator-detail-page {
  padding: 40px 0 96px;
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

.product-shell {
  display: grid;
  grid-template-columns: minmax(0, 1.35fr) minmax(330px, 0.65fr);
  gap: 16px;
  align-items: stretch;
  padding: 16px;
  background: rgba(22, 34, 49, 0.9);
  border: 1px solid rgba(103, 193, 245, 0.14);
  box-shadow: 0 18px 50px rgba(0, 0, 0, 0.28);
}

.product-heading {
  grid-column: 1 / -1;
  display: flex;
  justify-content: space-between;
  gap: 16px;
  align-items: center;
}

.product-heading h1 {
  margin: 0;
  color: #ffffff;
  font-size: clamp(1.5rem, 2.8vw, 2.35rem);
  line-height: 1.08;
}

.hero-chip {
  display: inline-flex;
  padding: 6px 10px;
  background: rgba(102, 192, 244, 0.16);
  color: #dbe8f4;
  font-weight: 700;
}

.hero-media {
  position: relative;
  min-height: 320px;
  height: min(60vh, 560px);
  max-height: min(60vh, 560px);
  overflow: hidden;
  background: #0e141b;
}

.hero-image,
.hero-fallback {
  width: 100%;
  height: 100%;
  display: block;
}

.hero-image {
  object-fit: cover;
}

.hero-fallback {
  display: grid;
  place-items: center;
  color: #66c0f4;
  font-size: 4rem;
  font-weight: 800;
}

.hero-copy {
  max-height: min(60vh, 560px);
  display: flex;
  flex-direction: column;
  gap: 16px;
  padding: 16px;
  overflow: auto;
  background: rgba(0, 0, 0, 0.18);
}

.creator-label {
  font-size: 0.78rem;
  letter-spacing: 0.08em;
  text-transform: uppercase;
  color: #8f98a0;
}

.creator-link {
  color: #ffffff;
  font-weight: 700;
}

.creator-link:hover {
  color: #66c0f4;
}

.meta-row,
.tag-row,
.hero-actions {
  display: flex;
  gap: 10px;
  flex-wrap: wrap;
}

.meta-pill,
.meta-stat {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 7px 11px;
  border-radius: 999px;
  font-size: 0.9rem;
}

.meta-pill {
  background: #66c0f4;
  color: #0b1118;
  font-weight: 700;
}

.meta-stat {
  background: rgba(103, 193, 245, 0.11);
  color: #c7d5e0;
}

.app-description {
  margin: 0;
  color: #dbe8f4;
  line-height: 1.7;
  white-space: pre-line;
}

.hero-actions {
  margin-top: auto;
}

.gallery-section {
  margin-top: 28px;
}

.gallery-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(200px, 1fr));
  gap: 16px;
}

.gallery-card {
  overflow: hidden;
  padding: 0;
  border: 1px solid rgba(103, 193, 245, 0.12);
  background: rgba(32, 50, 70, 0.72);
  color: #ffffff;
  cursor: pointer;
  text-align: left;
}

.gallery-card.active {
  border-color: #66c0f4;
  box-shadow: 0 0 0 2px rgba(102, 192, 244, 0.2);
}

.gallery-card img {
  width: 100%;
  aspect-ratio: 16 / 9;
  object-fit: contain;
  background: #0e141b;
  display: block;
}

.gallery-card span {
  display: block;
  padding: 12px;
}

.detail-state {
  display: grid;
  place-items: center;
  gap: 14px;
  min-height: 320px;
  padding: 32px;
  border: 1px solid var(--color-border);
  background: var(--color-surface);
  color: var(--color-text-muted);
  text-align: center;
}

.state-orb {
  width: 76px;
  height: 76px;
  border-radius: 999px;
  background:
    radial-gradient(circle at 30% 30%, rgba(255, 255, 255, 0.28), transparent 35%),
    linear-gradient(135deg, rgba(102, 192, 244, 0.4), rgba(77, 166, 255, 0.18));
}

@media (max-width: 960px) {
  .product-shell {
    grid-template-columns: 1fr;
  }

  .hero-media {
    height: min(46vh, 420px);
    max-height: min(46vh, 420px);
  }

  .hero-copy {
    max-height: none;
    overflow: visible;
  }
}
</style>
