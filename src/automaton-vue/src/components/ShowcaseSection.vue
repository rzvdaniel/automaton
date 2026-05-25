<script setup lang="ts">
import { computed, onMounted, watch } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'
import FeaturedAppsShowcase from '@/components/FeaturedAppsShowcase.vue'
import MarketplaceCarousel from '@/components/MarketplaceCarousel.vue'
import { resolveMarketplaceAssetUrl, useMarketplaceApps } from '@/composables/useMarketplaceApps'
import pacManImage from '@/assets/images/pac-man.png'
import snakeImage from '@/assets/images/snake.png'
import tetrisImage from '@/assets/images/tetris.png'
import flappyBirdsImage from '@/assets/images/flappy-birds.png'
import installRocketImage from '@/assets/images/install-rocket.png'
import type { MarketplaceAppCard } from '@/types/marketplace'

const props = defineProps<{ fullPage?: boolean }>()
const route = useRoute()
const router = useRouter()
const { activeCategory, apps, categories, featuredApps, loading, loadApps, loadCategories, search } =
  useMarketplaceApps(props.fullPage ? 100 : 10)

const fallbackApps: MarketplaceAppCard[] = [
  {
    id: 'fallback-pac-man',
    name: 'Pac-Man Editor and Game',
    creatorPublicUsername: 'razvan',
    creatorDisplayName: 'Razvan Predescu',
    summary: 'Playable game with editor-style customization and strong screenshots.',
    description: 'Playable game with editor-style customization and strong screenshots.',
    thumbnailUrl: pacManImage,
    category: 'Games',
    viewCount: 55,
    starCount: 0,
    forkCount: 0,
    isForkable: true,
    hasBackendExecution: false,
    isFeatured: true,
    screenshots: [
      { id: 'pac-snake', imageUrl: snakeImage, caption: 'Snake remix', sortOrder: 1 },
      { id: 'pac-tetris', imageUrl: tetrisImage, caption: 'Tetris builder', sortOrder: 2 },
      { id: 'pac-flappy', imageUrl: flappyBirdsImage, caption: 'Flappy Birds', sortOrder: 3 },
    ],
  },
  {
    id: 'fallback-snake',
    name: 'Snake Remix',
    creatorPublicUsername: 'automaton',
    creatorDisplayName: 'Automaton Monster',
    summary: 'A compact arcade app showing fast frontend generation.',
    description: 'A compact arcade app showing fast frontend generation.',
    thumbnailUrl: snakeImage,
    category: 'Games',
    viewCount: 41,
    starCount: 0,
    forkCount: 0,
    isForkable: true,
    hasBackendExecution: false,
    screenshots: [],
  },
  {
    id: 'fallback-tetris',
    name: 'Tetris Builder',
    creatorPublicUsername: 'automaton',
    creatorDisplayName: 'Automaton Monster',
    summary: 'Classic gameplay with a clean publishable web surface.',
    description: 'Classic gameplay with a clean publishable web surface.',
    thumbnailUrl: tetrisImage,
    category: 'Templates',
    viewCount: 38,
    starCount: 0,
    forkCount: 0,
    isForkable: true,
    hasBackendExecution: false,
    screenshots: [],
  },
  {
    id: 'fallback-flappy',
    name: 'Flappy Birds',
    creatorPublicUsername: 'automaton',
    creatorDisplayName: 'Automaton Monster',
    summary: 'Simple controls, strong screenshots, and an easy remix prompt.',
    description: 'Simple controls, strong screenshots, and an easy remix prompt.',
    thumbnailUrl: flappyBirdsImage,
    category: 'Games',
    viewCount: 32,
    starCount: 0,
    forkCount: 0,
    isForkable: true,
    hasBackendExecution: false,
    screenshots: [],
  },
  {
    id: 'fallback-sprint-poker',
    name: 'Sprint Poker',
    creatorPublicUsername: 'automaton',
    creatorDisplayName: 'Automaton Monster',
    summary: 'Planning poker rooms, reveal states, and team estimation.',
    description: 'Planning poker rooms, reveal states, and team estimation.',
    thumbnailUrl: pacManImage,
    category: 'Tools',
    viewCount: 27,
    starCount: 0,
    forkCount: 0,
    isForkable: true,
    hasBackendExecution: false,
    screenshots: [],
  },
  {
    id: 'fallback-cli-helper',
    name: 'CLI Setup Helper',
    creatorPublicUsername: 'automaton',
    creatorDisplayName: 'Automaton Monster',
    summary: 'Guided setup flow for getting agent builds ready.',
    description: 'Guided setup flow for getting agent builds ready.',
    thumbnailUrl: installRocketImage,
    category: 'Tools',
    viewCount: 27,
    starCount: 0,
    forkCount: 0,
    isForkable: true,
    hasBackendExecution: false,
    screenshots: [],
  },
  {
    id: 'fallback-json-studio',
    name: 'JSON Studio',
    creatorPublicUsername: 'automaton',
    creatorDisplayName: 'Automaton Monster',
    summary: 'Format, compare, validate, and share JSON snippets.',
    description: 'Format, compare, validate, and share JSON snippets.',
    thumbnailUrl: installRocketImage,
    category: 'Productivity',
    viewCount: 24,
    starCount: 0,
    forkCount: 0,
    isForkable: true,
    hasBackendExecution: false,
    screenshots: [],
  },
  {
    id: 'fallback-api-tester',
    name: 'API Tester',
    creatorPublicUsername: 'automaton',
    creatorDisplayName: 'Automaton Monster',
    summary: 'Send requests, inspect responses, and save small collections.',
    description: 'Send requests, inspect responses, and save small collections.',
    thumbnailUrl: tetrisImage,
    category: 'Tools',
    viewCount: 19,
    starCount: 0,
    forkCount: 0,
    isForkable: true,
    hasBackendExecution: false,
    screenshots: [],
  },
]

const carouselApps = computed(() => {
  const featured = featuredApps.value.slice(0, 12)
  if (featured.length > 0) return featured.slice(0, props.fullPage ? 12 : 10)
  return props.fullPage ? [] : apps.value.slice(0, 10)
})

const heading = computed(() => (props.fullPage ? 'Discover Apps' : 'Explore apps built with Automaton'))

const subtitle = computed(() =>
  props.fullPage
    ? 'Play, fork, and build from real Automaton projects.'
    : 'A rotating shelf of polished public apps, featured by category and ranked by community momentum.',
)

const heroApps = computed(() => {
  const featured = carouselApps.value
  if (featured.length > 0) return featured
  const realApps = apps.value.slice(0, 8)
  if (realApps.length > 0) return realApps
  return props.fullPage ? [] : fallbackApps
})

const displayedCategories = computed(() => {
  if (categories.value.length > 0) return categories.value
  if (props.fullPage) return []
  return [...new Set(fallbackApps.flatMap((app) => (app.category ? [app.category] : [])))]
})

const directoryApps = computed(() => {
  const source = props.fullPage ? apps.value : (apps.value.length > 0 ? apps.value : fallbackApps)
  if (!activeCategory.value) return source
  return source.filter((app) => app.category === activeCategory.value)
})

const categoryPills = computed(() => {
  const base = ['Featured', 'Live Apps', 'Forkable Apps']
  const dynamic = displayedCategories.value.filter((category) => !base.includes(category)).slice(0, 7)
  return [...base, ...dynamic]
})

function formatCount(value: number) {
  if (value >= 1000) return `${(value / 1000).toFixed(value >= 10000 ? 0 : 1)}k`
  return String(value)
}

function pluralize(value: number, singular: string, plural = `${singular}s`) {
  return `${formatCount(value)} ${value === 1 ? singular : plural}`
}

function updatedLabel(app: MarketplaceAppCard) {
  const source = app.updatedAt || app.publishedAt
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
}

function creatorLabel(app: MarketplaceAppCard) {
  return app.creatorDisplayName || app.creatorPublicUsername || 'Automaton creator'
}

function creatorInitials(app: MarketplaceAppCard) {
  return creatorLabel(app)
    .split(/\s+/)
    .map((part) => part.charAt(0))
    .join('')
    .slice(0, 2)
    .toUpperCase()
}

function appImageUrl(app: MarketplaceAppCard) {
  return app.thumbnailUrl ? resolveMarketplaceAssetUrl(app.thumbnailUrl) : ''
}

function openDetails(appId: string) {
  void router.push(`/apps/${appId}`)
}

function viewApp(app: MarketplaceAppCard) {
  openDetails(app.id)
}

function refreshApps() {
  void loadApps()
}

function normalizeCategory(category: string) {
  if (category === 'Featured' || category === 'Live Apps' || category === 'Forkable Apps') {
    return ''
  }

  return category
}

function applyRouteFilters() {
  if (!props.fullPage) return

  activeCategory.value =
    typeof route.query.category === 'string' ? normalizeCategory(route.query.category) : ''
}

function openCreator(publicUsername: string) {
  void router.push(`/${publicUsername}`)
}

onMounted(async () => {
  applyRouteFilters()
  await loadCategories()
  await loadApps()
})

watch(
  () => route.query.category,
  async () => {
    if (!props.fullPage) return
    applyRouteFilters()
    await loadApps()
  },
)
</script>

<template>
  <section id="apps" class="showcase-section" :class="{ 'showcase-section-full': props.fullPage }">
    <div class="container showcase-content">
      <template v-if="props.fullPage">
        <section v-if="heroApps.length > 0" class="featured-section" aria-label="Featured apps">
          <span class="section-label featured-eyebrow">Public apps</span>
          <div class="section-head">
            <h2>Featured apps</h2>
          </div>

          <FeaturedAppsShowcase
            class="showcase-carousel"
            :apps="heroApps"
            @select="openDetails"
          />
        </section>
      </template>

      <template v-else>
        <div class="showcase-header">
          <div>
            <span class="section-label">Marketplace</span>
            <h2 class="section-title">{{ heading }}</h2>
            <p class="section-subtitle showcase-subtitle">{{ subtitle }}</p>
          </div>
          <RouterLink to="/apps" class="btn btn-secondary showcase-link">
            Browse all apps
            <i class="pi pi-arrow-right" />
          </RouterLink>
        </div>

        <MarketplaceCarousel
          v-if="carouselApps.length > 0"
          class="showcase-carousel"
          :apps="carouselApps"
          compact
          @select="openDetails"
        />
      </template>

      <div v-if="loading" class="showcase-empty">
        <div class="empty-orb" />
        <p>Loading marketplace apps...</p>
      </div>

      <div v-else-if="directoryApps.length === 0" class="showcase-empty">
        <div class="empty-orb" />
        <p>No apps matched this category yet. Try another tab or search term.</p>
      </div>

      <section v-else-if="props.fullPage" class="directory-section" aria-label="Marketplace app directory">
        <div class="section-head">
          <h2>Trending apps</h2>
        </div>

        <div class="apps-grid">
          <article
            v-for="app in directoryApps"
            :key="app.id"
            class="app-card"
            :class="{ 'app-card-template': app.category?.toLowerCase().includes('template') }"
            role="link"
            tabindex="0"
            @click="openDetails(app.id)"
            @keydown.enter.prevent="openDetails(app.id)"
            @keydown.space.prevent="openDetails(app.id)"
          >
            <div class="app-media">
              <img
                v-if="appImageUrl(app)"
                :src="appImageUrl(app)"
                class="app-thumbnail"
                :alt="app.name"
              />
              <div v-else class="app-thumbnail app-thumbnail-fallback">
                <span>{{ app.name.charAt(0) }}</span>
              </div>
            </div>

            <div class="app-body">
              <div>
                <div class="badge-row">
                  <span class="badge badge-live">Live app</span>
                  <span v-if="app.isFeatured" class="badge badge-hot">Featured</span>
                </div>
                <h3 class="app-title">{{ app.name }}</h3>
                <p class="app-summary">{{ app.summary || app.description || 'Open the detail page to explore the app.' }}</p>
              </div>

              <div class="card-meta">
                <div class="creator-row">
                  <button
                    v-if="app.creatorPublicUsername"
                    type="button"
                    class="creator-link"
                    @click.stop="openCreator(app.creatorPublicUsername)"
                    @keydown.enter.stop
                    @keydown.space.stop
                  >
                    <span class="creator-avatar">{{ creatorInitials(app) }}</span>
                    <span>by {{ creatorLabel(app) }}</span>
                  </button>
                  <span v-else class="creator-link creator-link-static">
                    <span class="creator-avatar">{{ creatorInitials(app) }}</span>
                    <span>by {{ creatorLabel(app) }}</span>
                  </span>
                </div>

                <div class="app-meta-row" aria-label="App metadata">
                  <span><i class="pi pi-play" /> {{ pluralize(app.viewCount, 'play') }}</span>
                  <span>{{ updatedLabel(app) }}</span>
                </div>

                <div class="app-actions app-actions-single">
                  <button class="button" type="button" @click.stop="viewApp(app)">
                    <i class="pi pi-arrow-right" />
                    Open app
                  </button>
                </div>
              </div>
            </div>
          </article>
        </div>
      </section>

      <section v-if="props.fullPage" class="market-footer-grid" aria-label="Marketplace activity">
        <article class="activity-panel">
          <div class="section-head compact">
            <h2>Creator activity</h2>
            <p>New apps, forks, and updates give visitors a reason to come back.</p>
          </div>
          <div class="feed">
            <div v-for="app in directoryApps.slice(0, 4)" :key="`activity-${app.id}`" class="feed-item">
              <time>{{ app.isFeatured ? 'New' : 'Live' }}</time>
              <span>{{ creatorLabel(app) }} published "{{ app.name }}"</span>
              <span class="badge badge-live">{{ app.category || 'App' }}</span>
            </div>
          </div>
        </article>

        <aside class="runner-panel">
          <h2>Backend apps need a Runner</h2>
          <p>Static apps can be played, forked, and published freely. Backend apps can run on your own Docker runner today, with managed runners rolling out soon.</p>
          <div class="runner-options">
            <div class="runner-option"><span>Static apps</span><span>Free</span></div>
            <div class="runner-option"><span>Self-hosted Runner</span><span>Available today</span></div>
            <div class="runner-option"><span>Managed Runner</span><span>Coming soon</span></div>
          </div>
        </aside>
      </section>
    </div>
  </section>
</template>

<style scoped>
.showcase-section {
  padding: 0;
}

.showcase-section-full .showcase-content {
  width: min(1200px, calc(100% - 40px));
  max-width: none;
  padding-top: 22px;
  padding-right: 0;
  padding-left: 0;
}

.showcase-section-full {
  min-height: 100vh;
  background:
    radial-gradient(circle at 12% -6%, rgba(80, 215, 255, 0.14), transparent 30rem),
    radial-gradient(circle at 86% 2%, rgba(69, 230, 138, 0.11), transparent 28rem),
    linear-gradient(180deg, #090b10 0%, #07080b 46%, #050608 100%);
  color: #f5f7fb;
  font-family: Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
}

.showcase-header {
  display: flex;
  align-items: flex-end;
  justify-content: space-between;
  gap: 24px;
  margin-bottom: 28px;
}

.showcase-subtitle {
  max-width: 720px;
}

.showcase-link {
  white-space: nowrap;
}

.showcase-carousel {
  margin-bottom: 18px;
}

.featured-eyebrow {
  display: inline-flex;
  width: fit-content;
  padding: 6px 10px;
  border: 1px solid rgba(80, 215, 255, 0.22);
  border-radius: 999px;
  background: rgba(80, 215, 255, 0.08);
  color: #c4f2ff;
  font-size: 0.82rem;
  font-weight: 850;
  letter-spacing: 0;
  text-transform: none;
  margin-bottom: 12px;
}

.featured-section,
.directory-section,
.market-footer-grid {
  padding: 34px 0;
}

.section-head {
  display: flex;
  align-items: end;
  justify-content: space-between;
  gap: 28px;
  margin-bottom: 16px;
}

.section-head.compact {
  display: grid;
  gap: 7px;
  margin-bottom: 18px;
}

.section-head h2 {
  margin: 0;
  color: #f5f7fb;
  font-size: 1.6rem;
  line-height: 1.2;
  letter-spacing: -0.03em;
}

.section-head p {
  max-width: 430px;
  margin: 0;
  color: #9ba3b4;
  font-size: 0.96rem;
}

.apps-grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 16px;
}

.app-card {
  position: relative;
  cursor: pointer;
  overflow: hidden;
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 8px;
  background: rgba(255, 255, 255, 0.04);
  transition:
    transform 0.18s ease,
    border-color 0.18s ease,
    background 0.18s ease;
}

.app-card:hover {
  transform: translateY(-3px);
  border-color: rgba(69, 230, 138, 0.34);
  background: rgba(255, 255, 255, 0.06);
}

.app-card-template {
  border-color: rgba(69, 230, 138, 0.24);
  background:
    linear-gradient(180deg, rgba(69, 230, 138, 0.055), rgba(255, 255, 255, 0.035));
}

.app-media {
  background: #05070d;
}

.app-thumbnail {
  width: 100%;
  height: 240px;
  object-fit: cover;
  display: block;
}

.app-thumbnail-fallback {
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 3rem;
  font-weight: 800;
  color: rgba(255, 255, 255, 0.86);
}

.app-body {
  display: grid;
  gap: 14px;
  padding: 16px;
}

.badge-row {
  display: flex;
  flex-wrap: wrap;
  gap: 7px;
  margin-bottom: 9px;
}

.badge {
  padding: 4px 8px;
  border-radius: 999px;
  background: rgba(80, 215, 255, 0.12);
  color: #bdefff;
  font-size: 0.68rem;
  font-weight: 950;
  letter-spacing: 0.045em;
  text-transform: uppercase;
}

.badge-live {
  background: rgba(80, 215, 255, 0.12);
  color: #bdefff;
}

.badge-template {
  color: #45e68a;
  background: rgba(69, 230, 138, 0.14);
}

.badge-static {
  color: #d6dbe7;
  background: rgba(255, 255, 255, 0.08);
}

.badge-hot {
  color: #ffcb57;
  background: rgba(255, 203, 87, 0.13);
}

.app-title {
  margin: 0 0 6px;
  color: #f5f7fb;
  font-size: 1.03rem;
  line-height: 1.12;
  letter-spacing: -0.018em;
}

.creator-row {
  display: flex;
  align-items: center;
  min-width: 0;
}

.creator-avatar {
  display: inline-grid;
  place-items: center;
  width: 22px;
  height: 22px;
  flex: 0 0 auto;
  border-radius: 999px;
  background: linear-gradient(135deg, rgba(80, 215, 255, 0.3), rgba(69, 230, 138, 0.16));
  color: #f5f7fb;
  font-size: 0.72rem;
  font-weight: 900;
}

.creator-link {
  display: inline-flex;
  align-items: center;
  gap: 7px;
  min-width: 0;
  padding: 4px 6px;
  border: 0;
  border-radius: 8px;
  background: transparent;
  color: #50d7ff;
  font-size: 0.82rem;
  font-weight: 700;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  cursor: pointer;
  transition:
    color 0.18s ease,
    background 0.18s ease,
    transform 0.18s ease;
}

.creator-link:not(.creator-link-static):hover {
  color: #9ee8ff;
  background: rgba(80, 215, 255, 0.08);
  transform: translateY(-1px);
  text-decoration: underline;
  text-underline-offset: 0.16em;
}

.creator-link:not(.creator-link-static):hover .creator-avatar {
  box-shadow: 0 0 0 1px rgba(80, 215, 255, 0.22);
}

.creator-link-static {
  cursor: default;
}

.app-summary {
  min-height: 43px;
  margin: 0;
  color: #9ba3b4;
  font-size: 0.88rem;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
}

.card-meta {
  display: grid;
  gap: 10px;
}

.app-actions {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 7px;
}

.app-actions-single {
  grid-template-columns: 1fr;
}

.app-meta-row {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px;
  color: #9fb0c2;
  font-size: 0.78rem;
  font-weight: 850;
}

.app-meta-row span {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  color: #778196;
}

.app-meta-row i {
  font-size: 0.72rem;
}

.button {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 7px;
  min-height: 34px;
  padding: 0 12px;
  border: 1px solid rgba(255, 255, 255, 0.11);
  border-radius: 8px;
  background: rgba(3, 7, 14, 0.48);
  color: #dfe6f2;
  font-size: 0.82rem;
  font-weight: 850;
  cursor: pointer;
  box-shadow: inset 0 1px 0 rgba(255, 255, 255, 0.04);
  transition:
    border-color 0.18s ease,
    background 0.18s ease,
    color 0.18s ease,
    box-shadow 0.18s ease,
    transform 0.18s ease;
}

.button.primary {
  border-color: rgba(255, 255, 255, 0.11);
  background: rgba(3, 7, 14, 0.48);
  color: #dfe6f2;
  box-shadow: inset 0 1px 0 rgba(255, 255, 255, 0.04);
}

.button.ghost {
  background: rgba(255, 255, 255, 0.035);
  color: #b9c5d6;
}

.button:hover {
  transform: translateY(-1px);
  border-color: rgba(69, 230, 138, 0.38);
  background: rgba(69, 230, 138, 0.1);
  color: #eafff1;
  box-shadow:
    inset 0 1px 0 rgba(255, 255, 255, 0.06),
    0 10px 24px rgba(69, 230, 138, 0.1);
}

.button.ghost:hover {
  border-color: rgba(80, 215, 255, 0.32);
  background: rgba(80, 215, 255, 0.08);
  color: #ecfbff;
  box-shadow:
    inset 0 1px 0 rgba(255, 255, 255, 0.06),
    0 10px 24px rgba(80, 215, 255, 0.08);
}

.button i,
.button-icon {
  width: 0.82rem;
  height: 0.82rem;
  font-size: 0.76rem;
  flex: 0 0 auto;
}

.market-footer-grid {
  display: grid;
  grid-template-columns: 1.1fr 0.9fr;
  gap: 14px;
  padding-bottom: 54px;
}

.activity-panel,
.runner-panel {
  overflow: hidden;
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 8px;
  background: rgba(255, 255, 255, 0.04);
}

.activity-panel {
  padding: 18px 18px 0;
}

.feed {
  display: grid;
  gap: 1px;
  margin: 0 -18px;
  background: rgba(255, 255, 255, 0.1);
}

.feed-item {
  display: grid;
  grid-template-columns: auto 1fr auto;
  gap: 12px;
  align-items: center;
  padding: 13px 18px;
  background: rgba(16, 18, 24, 0.96);
  color: #c8ceda;
  font-size: 0.9rem;
}

.feed-item time {
  color: #45e68a;
  font-family: ui-monospace, SFMono-Regular, Consolas, monospace;
  font-size: 0.78rem;
  font-weight: 800;
}

.runner-panel {
  display: grid;
  align-content: space-between;
  gap: 20px;
  padding: 18px;
  background:
    linear-gradient(135deg, rgba(69, 230, 138, 0.08), rgba(80, 215, 255, 0.04)),
    rgba(255, 255, 255, 0.04);
}

.runner-panel h2 {
  margin: 0 0 7px;
  color: #f5f7fb;
  font-size: 1rem;
  line-height: 1.2;
  letter-spacing: -0.01em;
}

.runner-panel p {
  margin: 0;
  color: #9ba3b4;
}

.runner-options {
  display: grid;
  gap: 10px;
}

.runner-option {
  display: flex;
  justify-content: space-between;
  gap: 12px;
  padding: 13px;
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 8px;
  background: rgba(0, 0, 0, 0.12);
  color: #c8ceda;
  font-weight: 800;
}

.runner-option span:last-child {
  color: #9ba3b4;
  font-size: 0.84rem;
}

.showcase-empty {
  display: grid;
  place-items: center;
  gap: 14px;
  min-height: 260px;
  padding: 32px;
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 8px;
  background: rgba(255, 255, 255, 0.04);
  text-align: center;
  color: var(--color-text-muted);
}

.empty-orb {
  width: 76px;
  height: 76px;
  border-radius: 999px;
  background:
    radial-gradient(circle at 30% 30%, rgba(255, 255, 255, 0.28), transparent 35%),
    linear-gradient(135deg, rgba(124, 106, 245, 0.4), rgba(77, 166, 255, 0.18));
  box-shadow: 0 18px 50px rgba(124, 106, 245, 0.2);
}

@media (max-width: 900px) {
  .showcase-section-full .showcase-content {
    width: min(1200px, calc(100% - 40px));
    padding-top: 30px;
  }

  .market-footer-grid {
    grid-template-columns: 1fr;
  }

  .showcase-header {
    flex-direction: column;
    align-items: flex-start;
  }

  .apps-grid {
    grid-template-columns: repeat(2, 1fr);
  }

  .section-head {
    display: grid;
    justify-content: stretch;
  }
}

@media (max-width: 720px) {
  .showcase-section-full .showcase-content {
    padding-top: 22px;
  }

  .apps-hero h1 {
    font-size: clamp(2.2rem, 12vw, 3.6rem);
  }

  .apps-grid,
  .market-footer-grid {
    grid-template-columns: 1fr;
  }

  .app-actions {
    grid-template-columns: 1fr;
  }

  .feed-item {
    grid-template-columns: 1fr;
  }
}
</style>
