<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import MarketplaceAppRow from '@/components/MarketplaceAppRow.vue'
import StoreFooter from '@/components/StoreFooter.vue'
import StoreHeader from '@/components/StoreHeader.vue'
import { publicAppConfig } from '@/config/publicAppConfig'
import { useMarketplaceApps } from '@/composables/useMarketplaceApps'
import { useSiteTheme } from '@/composables/useSiteTheme'
import type { MarketplaceAppCard } from '@/types/marketplace'

// The marketplace returns at most this many apps; filtering happens in the page.
const MARKETPLACE_TAKE = 100
const FEATURED = 'Featured'

const route = useRoute()
const query = ref('')
const { apps, categories, loading, error, loadApps, loadCategories } = useMarketplaceApps(MARKETPLACE_TAKE)

useSiteTheme()

const publishDocsUrl = 'https://automaton.monster/docs/deploy#marketplace-listing'

// Older links used ?category=Featured or "Live Apps" to mean every app; keep them working.
const activeCategory = computed(() => {
  const value = typeof route.query.category === 'string' ? route.query.category : ''
  if (value === 'Live Apps') return ''
  if (value === FEATURED && !apps.value.some((app) => app.isFeatured)) return ''
  return value
})

const chips = computed(() => {
  const list = categories.value.filter((category) => category !== FEATURED && category !== 'Live Apps')
  return apps.value.some((app) => app.isFeatured) ? [FEATURED, ...list] : list
})

const installableOnly = computed(() => route.query.installable === '1')

function matchesInstallable(app: MarketplaceAppCard) {
  return !installableOnly.value || app.isForkable
}

function matchesCategory(app: MarketplaceAppCard) {
  if (!activeCategory.value) return true
  if (activeCategory.value === FEATURED) return Boolean(app.isFeatured)
  return app.category === activeCategory.value
}

function matchesQuery(app: MarketplaceAppCard) {
  const text = query.value.trim().toLowerCase()
  if (!text) return true
  return [app.name, app.summary, app.description, app.category, app.creatorDisplayName, app.creatorPublicUsername]
    .filter(Boolean)
    .join(' ')
    .toLowerCase()
    .includes(text)
}

const visibleApps = computed(() => apps.value.filter((app) => matchesCategory(app) && matchesInstallable(app) && matchesQuery(app)))
const isFiltered = computed(() => Boolean(activeCategory.value || installableOnly.value || query.value.trim()))
const countLabel = computed(() => {
  const count = visibleApps.value.length
  return `${count} ${count === 1 ? 'app' : 'apps'}`
})

function categoryLink(category: string) {
  return { path: '/', query: { ...(category ? { category } : {}), ...(installableOnly.value ? { installable: '1' } : {}) } }
}

const installableLink = computed(() => ({
  path: '/',
  query: { ...(activeCategory.value ? { category: activeCategory.value } : {}), ...(installableOnly.value ? {} : { installable: '1' }) },
}))

function clearSearch() {
  query.value = ''
}

onMounted(async () => {
  await Promise.all([loadCategories().catch(() => undefined), loadApps(MARKETPLACE_TAKE)])
})
</script>

<template>
  <div class="site apps-page">
    <StoreHeader />

    <main>
      <section class="band intro">
        <div class="wrap">
          <h1>{{ publicAppConfig.storeTitle }}</h1>
          <p class="lede">Apps published on this server.</p>
        </div>
      </section>

      <section class="band" aria-label="Find apps">
        <div class="wrap toolbar">
          <label class="search">
            <svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="11" cy="11" r="7" /><path d="M20 20l-4-4" /></svg>
            <input
              id="apps-search"
              v-model="query"
              type="search"
              placeholder="Search apps, creators or categories"
              aria-label="Search apps"
              autocomplete="off"
            />
          </label>
          <nav v-if="chips.length || apps.some((app) => app.isForkable)" class="chips" aria-label="Categories">
            <RouterLink
              v-if="apps.some((app) => app.isForkable)"
              :to="installableLink"
              class="chip"
              :class="{ active: installableOnly }"
              :aria-pressed="installableOnly"
            >Installable</RouterLink>
            <RouterLink :to="categoryLink('')" class="chip" :class="{ active: !activeCategory }">All</RouterLink>
            <RouterLink
              v-for="category in chips"
              :key="category"
              :to="categoryLink(category)"
              class="chip"
              :class="{ active: activeCategory === category }"
            >{{ category }}</RouterLink>
          </nav>
        </div>
      </section>

      <section class="band results" aria-labelledby="results-title">
        <div class="wrap">
          <h2 id="results-title" class="count" aria-live="polite">{{ loading && !apps.length ? 'Loading apps…' : countLabel }}</h2>

          <p v-if="error" class="state">
            Public apps could not be loaded right now. Refresh the page to try again.
          </p>
          <p v-else-if="!loading && apps.length === 0" class="state">
            No public apps yet. When an app is listed in the marketplace, it appears here.
          </p>
          <div v-else-if="!loading && visibleApps.length === 0" class="state">
            <p>No apps match {{ query.trim() ? `“${query.trim()}”` : 'this category' }}.</p>
            <div class="state-actions">
              <button v-if="query.trim()" class="btn small" type="button" @click="clearSearch">Clear search</button>
              <RouterLink v-if="activeCategory" class="btn small" :to="categoryLink('')">Show all apps</RouterLink>
            </div>
          </div>
          <ul v-else class="app-grid" :class="{ single: visibleApps.length === 1 }">
            <li v-for="app in visibleApps" :key="app.id"><MarketplaceAppRow :app="app" /></li>
          </ul>
          <p v-if="isFiltered && visibleApps.length" class="filter-note">
            Filtered from {{ apps.length }} public {{ apps.length === 1 ? 'app' : 'apps' }}.
            <RouterLink :to="categoryLink('')" @click="clearSearch">Show all</RouterLink>
          </p>
        </div>
      </section>

      <section class="band" aria-label="List your app">
        <div class="wrap publish">
          <p>
            Want your app here? Publish it from
            <a v-if="publicAppConfig.studioUrl" :href="publicAppConfig.studioUrl">Studio</a><template v-else>Studio</template>
            (<a :href="publishDocsUrl" target="_blank" rel="noopener">how publishing works</a>).
          </p>
        </div>
      </section>
    </main>

    <StoreFooter />
  </div>
</template>

<style scoped>
.intro .wrap {
  padding-block: 28px 12px;
  display: grid;
  gap: 4px;
}
.intro h1 {
  font-size: clamp(1.8rem, 4vw, 2.4rem);
  font-weight: 800;
}

.toolbar {
  display: grid;
  gap: 18px;
  padding-block: 8px 12px;
}
.search {
  display: flex;
  align-items: center;
  gap: 10px;
  max-width: 560px;
  padding: 9px 16px;
  border: 1px solid var(--line);
  border-radius: 999px;
  background: var(--card);
}
.search:focus-within {
  outline: 3px solid var(--signal);
  outline-offset: 2px;
}
.search svg {
  width: 20px;
  height: 20px;
  flex: none;
  fill: none;
  stroke: var(--ink-soft);
  stroke-width: 2;
  stroke-linecap: round;
}
.search input {
  flex: 1;
  min-width: 0;
  border: 0;
  outline: 0;
  background: transparent;
  color: var(--ink);
  font: 400 0.95rem var(--body);
}
.search input::placeholder {
  color: var(--ink-soft);
}
.chips {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}
.site .chip {
  font: 500 0.82rem var(--body);
  padding: 5px 12px;
  border-radius: 999px;
  border: 1px solid var(--line);
  color: var(--ink-soft);
  text-decoration: none;
  background: var(--card);
}
.site .chip:hover {
  color: var(--ink);
  border-color: var(--ink);
}
.site .chip.active {
  background: var(--ink);
  border-color: var(--ink);
  color: var(--paper);
}

.results .wrap {
  padding-block: 28px 56px;
  display: grid;
  gap: 18px;
}
.count {
  font: 600 0.75rem/1 var(--body);
  letter-spacing: 0.14em;
  text-transform: uppercase;
  color: var(--ink-soft);
}
.app-grid {
  list-style: none;
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 10px;
}
.app-grid.single {
  grid-template-columns: minmax(0, 560px);
}
.state {
  display: grid;
  gap: 14px;
  justify-items: start;
  padding: 26px;
  border-radius: 18px;
  border: 1px dashed var(--line);
  color: var(--ink-soft);
}
.state-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 10px;
}
.filter-note {
  color: var(--ink-soft);
  font-size: 0.92rem;
}

.publish {
  padding-block: 8px 56px;
  color: var(--ink-soft);
}

@media (max-width: 900px) {
  .app-grid,
  .app-grid.single {
    grid-template-columns: minmax(0, 1fr);
  }
}
</style>
