<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import MarketplaceAppRow from '@/components/site/MarketplaceAppRow.vue'
import SiteFooter from '@/components/site/SiteFooter.vue'
import SiteHeader from '@/components/site/SiteHeader.vue'
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

const visibleApps = computed(() => apps.value.filter((app) => matchesCategory(app) && matchesQuery(app)))
const isFiltered = computed(() => Boolean(activeCategory.value || query.value.trim()))
const countLabel = computed(() => {
  const count = visibleApps.value.length
  return `${count} ${count === 1 ? 'app' : 'apps'}`
})

function categoryLink(category: string) {
  return { path: '/apps', query: category ? { category } : {} }
}

function clearSearch() {
  query.value = ''
}

onMounted(async () => {
  await Promise.all([loadCategories().catch(() => undefined), loadApps(MARKETPLACE_TAKE)])
})
</script>

<template>
  <div class="site apps-page">
    <SiteHeader />

    <main>
      <section class="band intro">
        <div class="wrap">
          <p class="eyebrow">Public apps</p>
          <h1>Apps people run on Automaton</h1>
          <p class="lede">
            Apps published from Automaton installations. Open one, see who made it, or list your own.
          </p>
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
          <nav v-if="chips.length" class="chips" aria-label="Categories">
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

      <section class="band" aria-labelledby="publish-title">
        <div class="wrap">
          <div class="publish">
            <div class="section-head">
              <p class="eyebrow">Your app here</p>
              <h2 id="publish-title">List your own app</h2>
              <p>
                Deploy an app to your Automaton server, then open it in Studio and choose
                <strong>Publishing</strong> to list it in the marketplace.
              </p>
            </div>
            <div class="cta">
              <RouterLink class="btn primary" :to="{ path: '/', hash: '#start' }">Get started <span class="arrow" aria-hidden="true">→</span></RouterLink>
              <a class="btn" :href="publicAppConfig.docsUrl">Read the docs</a>
            </div>
          </div>
        </div>
      </section>
    </main>

    <SiteFooter />
  </div>
</template>

<style scoped>
.intro .wrap {
  padding-block: 40px 28px;
  display: grid;
  gap: 16px;
}
.intro h1 {
  font-size: clamp(2.4rem, 5.4vw, 4rem);
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
  padding: 12px 18px;
  border: 1.5px solid var(--ink);
  border-radius: 999px;
  background: var(--paper);
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
  font: 400 1rem var(--body);
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
  font: 500 0.9rem var(--body);
  padding: 7px 14px;
  border-radius: 999px;
  border: 1px solid var(--line);
  color: var(--ink-soft);
  text-decoration: none;
  background: var(--paper);
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
  gap: 12px;
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
  display: grid;
  grid-template-columns: minmax(0, 1.2fr) auto;
  gap: 32px;
  align-items: end;
  margin-block: 8px 72px;
  padding: 34px;
  border-radius: 26px;
  background: var(--panel);
}
.publish .cta {
  margin-top: 0;
}

@media (max-width: 900px) {
  .app-grid,
  .app-grid.single,
  .publish {
    grid-template-columns: minmax(0, 1fr);
  }
}

@media (max-width: 620px) {
  .publish {
    padding: 22px;
  }
}
</style>
