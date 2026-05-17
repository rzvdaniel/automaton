<script setup lang="ts">
import { computed, onMounted } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import MarketplaceCarousel from '@/components/MarketplaceCarousel.vue'
import { resolveMarketplaceAssetUrl, useMarketplaceApps } from '@/composables/useMarketplaceApps'

const props = defineProps<{ fullPage?: boolean }>()
const router = useRouter()
const { activeCategory, apps, categories, featuredApps, loading, loadApps, loadCategories, search } =
  useMarketplaceApps(props.fullPage ? 100 : 10)

const carouselApps = computed(() => {
  const featured = featuredApps.value.slice(0, 12)
  if (featured.length > 0) return featured.slice(0, props.fullPage ? 12 : 10)
  return props.fullPage ? [] : apps.value.slice(0, 10)
})

const heading = computed(() =>
  props.fullPage ? 'Discover what people are shipping with Automaton' : 'Explore apps built with Automaton',
)

const subtitle = computed(() =>
  props.fullPage
    ? 'Browse community-made tools by category, jump into the most loved projects, and open any app in one click.'
    : 'A rotating shelf of polished public apps, featured by category and ranked by community momentum.',
)

function openDetails(appId: string) {
  void router.push(`/apps/${appId}`)
}

function refreshApps() {
  void loadApps()
}

function openCreator(publicUsername: string) {
  void router.push(`/${publicUsername}`)
}

onMounted(async () => {
  await loadCategories()
  await loadApps()
})
</script>

<template>
  <section id="apps" class="showcase-section" :class="{ 'showcase-section-full': props.fullPage }">
    <div v-if="props.fullPage" class="showcase-toolbar">
      <div class="container toolbar-inner">
        <div class="category-tabs" role="tablist" aria-label="Marketplace categories">
          <button
            class="category-tab"
            :class="{ 'category-tab-active': activeCategory === '' }"
            type="button"
            @click="activeCategory = ''; loadApps()"
          >
            All
          </button>
          <button
            v-for="category in categories"
            :key="category"
            class="category-tab"
            :class="{ 'category-tab-active': activeCategory === category }"
            type="button"
            @click="activeCategory = category; loadApps()"
          >
            {{ category }}
          </button>
        </div>

        <label class="search-bar">
          <i class="pi pi-search search-icon" />
          <input
            v-model="search"
            class="search-input"
            type="search"
            placeholder="Search apps"
            @keyup.enter="refreshApps"
          />
          <button type="button" class="search-button" aria-label="Search apps" @click="refreshApps">
            <i class="pi pi-arrow-right" />
          </button>
        </label>
      </div>
    </div>

    <div class="container showcase-content">
      <div class="showcase-header">
        <div>
          <span class="section-label">Marketplace</span>
          <h2 class="section-title">{{ heading }}</h2>
          <p class="section-subtitle showcase-subtitle">{{ subtitle }}</p>
        </div>
        <RouterLink v-if="!props.fullPage" to="/apps" class="btn btn-secondary showcase-link">
          Browse all apps
          <i class="pi pi-arrow-right" />
        </RouterLink>
      </div>

      <MarketplaceCarousel
        v-if="carouselApps.length > 0"
        class="showcase-carousel"
        :apps="carouselApps"
        :compact="!props.fullPage"
        @select="openDetails"
      />

      <div v-if="loading" class="showcase-empty">
        <div class="empty-orb" />
        <p>Loading marketplace apps...</p>
      </div>

      <div v-else-if="apps.length === 0" class="showcase-empty">
        <div class="empty-orb" />
        <p>No apps matched this category yet. Try another tab or search term.</p>
      </div>

      <div v-else-if="props.fullPage" class="apps-grid">
        <article
          v-for="app in apps"
          :key="app.id"
          class="app-card"
          role="link"
          tabindex="0"
          @click="openDetails(app.id)"
          @keydown.enter.prevent="openDetails(app.id)"
          @keydown.space.prevent="openDetails(app.id)"
        >
          <div class="app-media">
            <img
              v-if="app.thumbnailUrl"
              :src="resolveMarketplaceAssetUrl(app.thumbnailUrl)"
              class="app-thumbnail"
              :alt="app.name"
            />
            <div v-else class="app-thumbnail app-thumbnail-fallback">
              <span>{{ app.name.charAt(0) }}</span>
            </div>
            <div class="app-overlay">
              <span v-if="app.isFeatured" class="overlay-badge">Featured</span>
            </div>
          </div>

          <div class="app-body">
            <div class="app-topline">
              <span v-if="app.category" class="tag">{{ app.category }}</span>
              <span class="app-metric">⭐ {{ app.starCount }}</span>
              <span class="app-metric">👁 {{ app.viewCount }}</span>
            </div>

            <div>
              <h3 class="app-title">{{ app.name }}</h3>
              <button
                v-if="app.creatorPublicUsername"
                type="button"
                class="creator-link"
                @click.stop="openCreator(app.creatorPublicUsername)"
                @keydown.enter.stop
                @keydown.space.stop
              >
                by {{ app.creatorDisplayName || app.creatorPublicUsername }}
              </button>
              <p class="app-summary">{{ app.summary || app.description || 'Open the detail page to explore the app.' }}</p>
            </div>
          </div>
        </article>
      </div>
    </div>
  </section>
</template>

<style scoped>
.showcase-section {
  padding: 0px;
}

.showcase-section-full .showcase-content {
  padding-top: 28px;
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

.showcase-toolbar {
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
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  padding: 6px 8px;
  border: 1px solid rgba(124, 106, 245, 0.18);
  background:
    linear-gradient(135deg, rgba(35, 28, 70, 0.78), rgba(14, 28, 43, 0.78)),
    rgba(17, 17, 24, 0.86);
  box-shadow: 0 12px 26px rgba(0, 0, 0, 0.2);
}

.showcase-carousel {
  margin-bottom: 32px;
}

.category-tabs {
  display: flex;
  gap: 6px;
  min-width: 0;
  overflow-x: auto;
}

.category-tab {
  padding: 7px 12px;
  border: 0;
  background: transparent;
  color: #c7d5e0;
  font-size: 0.88rem;
  font-weight: 700;
  white-space: nowrap;
  cursor: pointer;
  transition: all 0.2s ease;
}

.category-tab:hover {
  color: #ffffff;
  background: rgba(124, 106, 245, 0.16);
}

.category-tab-active {
  color: #ffffff;
  background: linear-gradient(135deg, rgba(124, 106, 245, 0.34), rgba(77, 166, 255, 0.2));
}

.search-bar {
  display: flex;
  align-items: center;
  gap: 8px;
  width: min(380px, 40vw);
  min-width: 0;
  padding: 3px 8px;
  border: 1px solid rgba(124, 106, 245, 0.18);
  background: rgba(10, 10, 15, 0.66);
  color: var(--color-blue);
}

.search-icon {
  color: var(--color-blue);
}

.search-input {
  width: 100%;
  min-width: 0;
  background: transparent;
  border: none;
  color: var(--color-text);
  font-size: 0.92rem;
  line-height: 1.2;
  outline: none;
}

.search-input::placeholder {
  color: var(--color-text-muted);
}

.search-button {
  display: inline-grid;
  place-items: center;
  width: 28px;
  height: 24px;
  flex: 0 0 auto;
  padding: 0;
  border: 0;
  background: linear-gradient(135deg, var(--color-primary), var(--color-blue));
  color: #ffffff;
  font-size: 0.82rem;
  line-height: 1;
  cursor: pointer;
}

.apps-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(260px, 1fr));
  gap: 20px;
}

.app-card {
  display: grid;
  cursor: pointer;
  overflow: hidden;
  min-height: 100%;
  border: 1px solid rgba(255, 255, 255, 0.08);
  border-radius: 24px;
  background:
    radial-gradient(circle at top right, rgba(77, 166, 255, 0.18), transparent 30%),
    linear-gradient(180deg, rgba(255, 255, 255, 0.04), rgba(255, 255, 255, 0.02)),
    var(--color-surface);
  box-shadow: 0 18px 60px rgba(0, 0, 0, 0.28);
  transition: transform 0.24s ease, border-color 0.24s ease, box-shadow 0.24s ease;
}

.app-card:hover {
  transform: translateY(-6px);
  border-color: rgba(124, 106, 245, 0.32);
  box-shadow: 0 28px 80px rgba(0, 0, 0, 0.36);
}

.app-media {
  position: relative;
  display: grid;
  place-items: center;
  aspect-ratio: 1 / 1;
  overflow: hidden;
  padding: 10px;
  background: #05070d;
}

.app-thumbnail {
  width: 100%;
  height: 100%;
  min-width: 0;
  min-height: 0;
  object-fit: contain;
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

.app-overlay {
  position: absolute;
  inset: 0;
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  padding: 16px;
  background: linear-gradient(180deg, rgba(10, 10, 15, 0.08), rgba(10, 10, 15, 0.72));
}

.overlay-badge,
.overlay-link {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 8px 10px;
  border-radius: 999px;
  font-size: 0.78rem;
  font-weight: 700;
}

.overlay-badge {
  background: rgba(255, 255, 255, 0.14);
  color: #fff;
  backdrop-filter: blur(10px);
}

.app-body {
  display: grid;
  gap: 18px;
  padding: 18px;
}

.app-topline {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}

.app-metric {
  color: var(--color-text-muted);
  font-size: 0.82rem;
  font-weight: 600;
}

.app-title {
  font-size: 1.08rem;
  line-height: 1.2;
  margin-bottom: 8px;
}

.creator-link {
  margin: -2px 0 8px;
  padding: 0;
  border: 0;
  background: transparent;
  color: var(--color-blue);
  font-size: 0.82rem;
  font-weight: 700;
  cursor: pointer;
}

.creator-link:hover {
  color: #7cc3ff;
}

.app-summary {
  color: var(--color-text-muted);
  font-size: 0.92rem;
  display: -webkit-box;
  -webkit-line-clamp: 3;
  -webkit-box-orient: vertical;
  overflow: hidden;
}

.showcase-empty {
  display: grid;
  place-items: center;
  gap: 14px;
  min-height: 260px;
  padding: 32px;
  border: 1px solid var(--color-border);
  border-radius: 28px;
  background:
    radial-gradient(circle at top, rgba(124, 106, 245, 0.12), transparent 42%),
    var(--color-surface);
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
  .showcase-header {
    flex-direction: column;
    align-items: flex-start;
  }
}

@media (max-width: 720px) {
  .showcase-section-full .showcase-content {
    padding-top: 22px;
  }

  .toolbar-inner {
    display: grid;
    gap: 8px;
  }

  .search-bar {
    width: 100%;
  }

  .category-tabs {
    padding-bottom: 2px;
  }

  .search-button {
    width: 30px;
  }

  .apps-grid {
    grid-template-columns: 1fr;
  }
}
</style>
