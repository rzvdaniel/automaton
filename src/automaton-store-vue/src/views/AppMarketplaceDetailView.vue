<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import AppMark from '@/components/AppMark.vue'
import MarketplaceAppRow from '@/components/MarketplaceAppRow.vue'
import StoreFooter from '@/components/StoreFooter.vue'
import StoreHeader from '@/components/StoreHeader.vue'
import { getPublishedAppUrl, publicAppConfig } from '@/config/publicAppConfig'
import { resolveMarketplaceAssetUrl } from '@/composables/useMarketplaceApps'
import { useSiteTheme } from '@/composables/useSiteTheme'
import type { MarketplaceAppCard, MarketplaceAppDetails } from '@/types/marketplace'

type GalleryImage = { id: string; imageUrl: string; caption: string }

const route = useRoute()
const marketplaceApiBaseUrl = `${publicAppConfig.apiBaseUrl}api/public/marketplace`

const app = ref<MarketplaceAppDetails | null>(null)
const creatorApps = ref<MarketplaceAppCard[]>([])
const loading = ref(true)
const errorMessage = ref('')
const shareMessage = ref('')
const selectedIndex = ref(0)
const isLightboxOpen = ref(false)

useSiteTheme()

// The cover comes first, then the screenshots, without repeating an image.
const gallery = computed<GalleryImage[]>(() => {
  if (!app.value) return []
  const images: GalleryImage[] = [
    ...(app.value.thumbnailUrl ? [{ id: `${app.value.id}-cover`, imageUrl: app.value.thumbnailUrl, caption: `${app.value.name} cover` }] : []),
    ...app.value.screenshots
      .slice()
      .sort((a, b) => a.sortOrder - b.sortOrder)
      .map((shot) => ({ id: shot.id, imageUrl: shot.imageUrl, caption: shot.caption || `${app.value?.name} screenshot` })),
  ]
  return images.filter((image, index) => images.findIndex((other) => other.imageUrl === image.imageUrl) === index)
})
const selectedImage = computed(() => gallery.value[selectedIndex.value] ?? null)

const launchUrl = computed(() => (app.value?.canLaunch && app.value.hasWebPage !== false && app.value.publicUrl ? getPublishedAppUrl(app.value.publicUrl) : ''))
const installUrl = computed(() => (app.value?.canRemix && app.value.hasWebPage !== false && publicAppConfig.studioUrl ? `${publicAppConfig.studioUrl}apps/install/${app.value.id}` : ''))
const creatorName = computed(() => app.value?.creatorDisplayName || app.value?.creatorPublicUsername || '')
const summary = computed(() => app.value?.summary || app.value?.description || 'A public Automaton app.')
const description = computed(() => app.value?.description || app.value?.summary || '')
const tags = computed(() => app.value?.tags?.split(',').map((tag) => tag.trim()).filter(Boolean) ?? [])
const otherCreatorApps = computed(() => creatorApps.value.filter((other) => other.id !== app.value?.id))

function formatDate(value?: string) {
  if (!value) return ''
  const date = new Date(value)
  return Number.isNaN(date.getTime())
    ? ''
    : date.toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' })
}

function count(value: number, singular: string, plural = `${singular}s`) {
  const shown = value >= 1000 ? `${(value / 1000).toFixed(value >= 10000 ? 0 : 1)}k` : String(value)
  return `${shown} ${value === 1 ? singular : plural}`
}

function showImage(index: number) {
  const total = gallery.value.length
  if (total === 0) return
  selectedIndex.value = (index + total) % total
}

function openLightbox() {
  if (selectedImage.value) isLightboxOpen.value = true
}

function closeLightbox() {
  isLightboxOpen.value = false
}

function handleKeydown(event: KeyboardEvent) {
  if (!isLightboxOpen.value) return
  if (event.key === 'Escape') closeLightbox()
  if (event.key === 'ArrowLeft') showImage(selectedIndex.value - 1)
  if (event.key === 'ArrowRight') showImage(selectedIndex.value + 1)
}

async function shareApp() {
  shareMessage.value = ''
  try {
    if (navigator.share) {
      await navigator.share({ title: app.value?.name || 'Automaton app', url: window.location.href })
      return
    }
    await navigator.clipboard.writeText(window.location.href)
    shareMessage.value = 'Link copied.'
  } catch {
    shareMessage.value = 'Could not share this app. Copy the address from your browser instead.'
  }
}

async function trackLaunch(appId: string) {
  try {
    await fetch(`${marketplaceApiBaseUrl}/apps/${appId}/launch`, { method: 'POST', keepalive: true })
    if (app.value?.id === appId) app.value.viewCount += 1
  } catch {
    // Opening the app must not depend on analytics.
  }
}

function launchApp() {
  if (!app.value || !launchUrl.value) return
  window.open(launchUrl.value, '_blank', 'noopener')
  void trackLaunch(app.value.id)
}

async function loadCreatorApps(appId: string) {
  try {
    const response = await fetch(`${marketplaceApiBaseUrl}/apps/${appId}/creator-apps`)
    creatorApps.value = response.ok ? await response.json() : []
  } catch {
    creatorApps.value = []
  }
}

async function loadApp() {
  loading.value = true
  errorMessage.value = ''
  creatorApps.value = []
  selectedIndex.value = 0
  try {
    const response = await fetch(`${marketplaceApiBaseUrl}/apps/${route.params.appId}`)
    if (!response.ok) {
      app.value = null
      errorMessage.value = response.status === 404
        ? 'This app is no longer listed in the marketplace.'
        : 'This app could not be loaded right now. Refresh the page to try again.'
      return
    }
    app.value = await response.json()
    void loadCreatorApps(String(route.params.appId))
  } catch {
    app.value = null
    errorMessage.value = 'This app could not be loaded right now. Refresh the page to try again.'
  } finally {
    loading.value = false
  }
}

watch(() => route.params.appId, () => {
  closeLightbox()
  void loadApp()
})
watch(isLightboxOpen, (open) => {
  document.body.style.overflow = open ? 'hidden' : ''
})
onMounted(() => {
  window.addEventListener('keydown', handleKeydown)
  void loadApp()
})
onBeforeUnmount(() => {
  window.removeEventListener('keydown', handleKeydown)
  document.body.style.overflow = ''
})
</script>

<template>
  <div class="site detail-page">
    <StoreHeader />

    <main>
      <section class="band">
        <div class="wrap top-area">
          <p class="eyebrow crumbs">
            <RouterLink class="back" to="/"><span aria-hidden="true">←</span> All apps</RouterLink>
            <template v-if="app?.category"> · {{ app.category }}</template>
          </p>

          <p v-if="loading" class="state">Loading app…</p>

          <div v-else-if="errorMessage" class="state">
            <p>{{ errorMessage }}</p>
            <RouterLink class="btn small" to="/">Browse public apps</RouterLink>
          </div>

          <div v-else-if="app" class="hero" :class="{ plain: !selectedImage }">
            <div class="hero-text">
              <div class="badges">
                <span class="badge">{{ app.hasWebPage === false ? 'Installable' : app.canLaunch ? 'Live app' : 'Media preview' }}</span>
                <span v-if="app.isFeatured" class="badge signal">Featured</span>
              </div>
              <div class="title">
                <AppMark :id="app.id" :name="app.name" :image-url="app.thumbnailUrl" :size="56" />
                <h1>{{ app.name }}</h1>
              </div>
              <p v-if="creatorName" class="by">
                by
                <RouterLink v-if="app.creatorPublicUsername" :to="`/creators/${app.creatorPublicUsername}`">{{ creatorName }}</RouterLink>
                <template v-else>{{ creatorName }}</template>
              </p>
              <p class="lede">{{ summary }}</p>
              <div class="cta">
                <button v-if="launchUrl" class="btn primary" type="button" @click="launchApp">
                  Open app <span class="arrow" aria-hidden="true">↗</span>
                </button>
                <a v-if="installUrl" class="btn" :href="installUrl">Install on your server</a>
                <button class="btn" type="button" @click="shareApp">Share</button>
              </div>
              <p v-if="!launchUrl && app.hasWebPage === false" class="note">This app has no web page: people install it from Studio on their own server.</p>
              <p v-else-if="!launchUrl" class="note">This app has a private runtime. Its screenshots show what it does.</p>
              <p v-if="shareMessage" class="note" role="status">{{ shareMessage }}</p>
              <div v-if="tags.length" class="feats">
                <span v-for="tag in tags" :key="tag" class="feat">{{ tag }}</span>
              </div>
              <dl class="facts">
                <div>
                  <dt>Opened</dt>
                  <dd>{{ count(app.viewCount, 'time') }}</dd>
                </div>
                <div v-if="app.category">
                  <dt>Category</dt>
                  <dd>{{ app.category }}</dd>
                </div>
                <div v-if="app.canRemix">
                  <dt>Forks</dt>
                  <dd>{{ count(app.forkCount, 'fork') }}</dd>
                </div>
                <div v-if="formatDate(app.publishedAt)">
                  <dt>Published</dt>
                  <dd>{{ formatDate(app.publishedAt) }}</dd>
                </div>
                <div v-if="formatDate(app.updatedAt)">
                  <dt>Updated</dt>
                  <dd>{{ formatDate(app.updatedAt) }}</dd>
                </div>
              </dl>
            </div>

            <div v-if="selectedImage" class="gallery">
              <button
                class="stage"
                type="button"
                :aria-label="`View ${selectedImage.caption} larger`"
                @click="openLightbox"
              >
                <img :src="resolveMarketplaceAssetUrl(selectedImage.imageUrl)" :alt="selectedImage.caption" />
              </button>

              <div v-if="gallery.length > 1" class="thumbs" role="list" aria-label="Screenshots">
                <button
                  v-for="(image, index) in gallery"
                  :key="image.id"
                  class="thumb"
                  :class="{ active: index === selectedIndex }"
                  type="button"
                  role="listitem"
                  :aria-label="`Show ${image.caption}`"
                  :aria-current="index === selectedIndex"
                  @click="showImage(index)"
                >
                  <img :src="resolveMarketplaceAssetUrl(image.imageUrl)" alt="" loading="lazy" />
                </button>
              </div>
              <p class="cap">{{ gallery.length > 1 ? `${selectedIndex + 1} / ${gallery.length} · ` : '' }}{{ selectedImage.caption }}</p>
            </div>
          </div>
        </div>
      </section>

      <template v-if="app && !loading">
        <section class="band" aria-labelledby="about-title">
          <div class="wrap about">
            <div class="section-head">
              <h2 id="about-title">About</h2>
              <p v-if="description">{{ description }}</p>
              <p v-else>The creator has not written a description yet. Open the app to see what it does.</p>
            </div>
          </div>
        </section>

        <section v-if="app.catalogOrigin" class="band" aria-labelledby="own-server-title">
          <div class="wrap">
            <div class="own-server">
              <h2 id="own-server-title">Run it on your own server</h2>
              <p>{{ app.name }} is in the Automaton catalog ({{ app.catalogOrigin.catalog }}, {{ app.catalogOrigin.appPath }}). Ask your AI:</p>
              <code>install {{ app.name }} from the Automaton catalog</code>
            </div>
          </div>
        </section>

        <section v-if="app.forksAndRemixes?.length" class="band" aria-labelledby="forks-title">
          <div class="wrap list-block">
            <div class="section-head">
              <h2 id="forks-title">Built from {{ app.name }}</h2>
            </div>
            <ul class="rows">
              <li v-for="remix in app.forksAndRemixes" :key="remix.id"><MarketplaceAppRow :app="remix" /></li>
            </ul>
          </div>
        </section>

        <section v-if="otherCreatorApps.length" class="band" aria-labelledby="more-title">
          <div class="wrap list-block">
            <div class="section-head">
              <h2 id="more-title">More from {{ creatorName || 'this creator' }}</h2>
            </div>
            <ul class="rows">
              <li v-for="other in otherCreatorApps" :key="other.id"><MarketplaceAppRow :app="other" /></li>
            </ul>
          </div>
        </section>
      </template>
    </main>

    <StoreFooter />

    <div
      v-if="isLightboxOpen && selectedImage"
      class="lightbox"
      role="dialog"
      aria-modal="true"
      :aria-label="`${app?.name} screenshots`"
      @click.self="closeLightbox"
    >
      <figure>
        <img :src="resolveMarketplaceAssetUrl(selectedImage.imageUrl)" :alt="selectedImage.caption" />
        <figcaption>
          <span>{{ gallery.length > 1 ? `${selectedIndex + 1} / ${gallery.length} · ` : '' }}{{ selectedImage.caption }}</span>
          <span class="lightbox-actions">
            <button v-if="gallery.length > 1" class="btn small" type="button" aria-label="Previous screenshot" @click="showImage(selectedIndex - 1)">←</button>
            <button v-if="gallery.length > 1" class="btn small" type="button" aria-label="Next screenshot" @click="showImage(selectedIndex + 1)">→</button>
            <button class="btn small" type="button" @click="closeLightbox">Close</button>
          </span>
        </figcaption>
      </figure>
    </div>
  </div>
</template>

<style scoped>
.top-area {
  padding-block: 8px 24px;
  display: grid;
  gap: 16px;
}
.crumbs {
  text-transform: uppercase;
}
.site .back {
  color: inherit;
  text-decoration: none;
}
.site .back:hover {
  text-decoration: underline;
}

.state {
  display: grid;
  gap: 14px;
  justify-items: start;
  padding: 20px;
  border-radius: 16px;
  border: 1px dashed var(--line);
  color: var(--ink-soft);
}

.hero {
  display: grid;
  grid-template-columns: minmax(0, 5fr) minmax(0, 7fr);
  gap: 34px;
  align-items: start;
}
.hero.plain {
  grid-template-columns: minmax(0, 40rem);
}
.title {
  min-width: 0;
  display: flex;
  align-items: center;
  gap: 14px;
}
.hero-text {
  display: grid;
  gap: 12px;
  align-content: start;
}
.badges {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}
.badge {
  font: 600 0.65rem var(--body);
  letter-spacing: 0.1em;
  text-transform: uppercase;
  padding: 3px 8px;
  border-radius: 999px;
  border: 1px solid var(--line);
  color: var(--ink-soft);
}
.badge.signal {
  background: var(--signal);
  border-color: var(--signal);
  color: var(--on-signal);
}
.hero h1 {
  min-width: 0;
  overflow-wrap: anywhere;
  font-size: clamp(1.8rem, 4vw, 2.4rem);
  font-weight: 800;
}
.by {
  color: var(--ink-soft);
}
.hero .cta {
  margin-top: 6px;
}
.note {
  color: var(--ink-soft);
  font-size: 0.92rem;
}

.gallery {
  display: grid;
  gap: 12px;
  min-width: 0;
}
.stage {
  display: block;
  width: 100%;
  max-width: 100%;
  aspect-ratio: 16 / 9;
  padding: 0;
  border: 1px solid var(--line);
  border-radius: 12px;
  overflow: hidden;
  background: var(--panel);
  cursor: zoom-in;
}
.stage img {
  width: 100%;
  height: 100%;
  object-fit: cover;
  display: block;
}
.thumbs {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(min(100%, 96px), 1fr));
  gap: 10px;
}
.thumb {
  aspect-ratio: 16 / 10;
  padding: 0;
  border-radius: 8px;
  overflow: hidden;
  border: 2px solid transparent;
  background: var(--panel);
  cursor: pointer;
  opacity: 0.75;
}
.thumb:hover {
  opacity: 1;
}
.thumb.active {
  opacity: 1;
  border-color: var(--signal);
}
.thumb img {
  width: 100%;
  height: 100%;
  object-fit: cover;
  display: block;
}

.rule {
  border: 0;
  border-top: 1px solid var(--line);
}

.about {
  padding-block: 26px 32px;
  border-top: 1px solid var(--line);
}
.feats {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}
.feat {
  font: 500 0.82rem var(--body);
  padding: 5px 11px;
  border-radius: 999px;
  background: color-mix(in srgb, var(--signal) 16%, var(--paper));
  color: var(--signal-ink);
}
.facts {
  margin-top: 8px;
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  border-top: 1px solid var(--line);
}
.facts div {
  display: grid;
  gap: 2px;
  padding: 10px 0;
  border-bottom: 1px solid var(--line);
}
.facts dt {
  font: 0.7rem var(--mono);
  color: var(--ink-soft);
  text-transform: uppercase;
  letter-spacing: 0.08em;
}
.facts dd {
  font-weight: 500;
  overflow-wrap: anywhere;
}
.cap {
  font: 0.78rem var(--mono);
  color: var(--ink-soft);
}

.list-block {
  padding-block: 8px 40px;
  display: grid;
  gap: 14px;
}
.rows {
  list-style: none;
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(min(100%, 250px), 1fr));
  gap: 18px;
}

.lightbox {
  position: fixed;
  inset: 0;
  z-index: 50;
  display: grid;
  place-items: center;
  padding: max(16px, env(safe-area-inset-top, 0px)) max(16px, env(safe-area-inset-right, 0px)) max(16px, env(safe-area-inset-bottom, 0px)) max(16px, env(safe-area-inset-left, 0px));
  background: rgba(10, 10, 12, 0.88);
}
.lightbox figure {
  display: grid;
  gap: 12px;
  max-width: min(1200px, 100%);
}
.lightbox img {
  max-width: 100%;
  max-height: 80vh;
  border-radius: 14px;
  display: block;
}
.lightbox figcaption {
  display: flex;
  flex-wrap: wrap;
  justify-content: space-between;
  align-items: center;
  gap: 12px;
  color: #f1f0eb;
  font-size: 0.92rem;
}
.lightbox-actions {
  display: flex;
  gap: 8px;
}
.site .lightbox .btn {
  color: #f1f0eb;
  border-color: #f1f0eb;
}

@media (max-width: 900px) {
  .hero {
    grid-template-columns: minmax(0, 1fr);
  }
  .hero {
    gap: 28px;
  }
}
.own-server {
  padding: 1.5rem;
  border-radius: 12px;
  background: #15171c;
  color: #f1f1ee;
}
.own-server h2 {
  margin: 0 0 0.5rem;
}
.own-server code {
  display: block;
  padding: 0.7rem 0.9rem;
  border-radius: 8px;
  background: #0b0c0f;
  font: 0.85rem var(--mono);
}
</style>
