<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import { publicAppConfig } from '@/config/publicAppConfig'
import type { MarketplaceAppCard } from '@/types/marketplace'

const props = defineProps<{ fullPage?: boolean }>()
const router = useRouter()
const marketplaceApiBaseUrl = `${publicAppConfig.apiBaseUrl}api/public/marketplace`

function getLaunchUrl(publicUrl?: string) {
  return publicUrl ? `${publicAppConfig.studioUrl}web/${publicUrl}` : null
}

const categories = ref<string[]>([])
const apps = ref<MarketplaceAppCard[]>([])
const activeCategory = ref('')
const search = ref('')
const loading = ref(false)

const heading = computed(() =>
  props.fullPage ? 'Discover what people are shipping with Automaton' : 'Explore apps built with Automaton',
)

const subtitle = computed(() =>
  props.fullPage
    ? 'Browse community-made tools by category, jump into the most loved projects, and open any app in one click.'
    : 'A rotating shelf of polished public apps, featured by category and ranked by community momentum.',
)

async function loadApps() {
  loading.value = true

  try {
    const query = new URLSearchParams()

    if (activeCategory.value) {
      query.set('category', activeCategory.value)
    }

    if (search.value.trim()) {
      query.set('search', search.value.trim())
    }

    query.set('take', props.fullPage ? '100' : '8')

    const response = await fetch(`${marketplaceApiBaseUrl}/apps?${query.toString()}`)
    apps.value = await response.json()
  } finally {
    loading.value = false
  }
}

function openDetails(appId: string) {
  void router.push(`/apps/${appId}`)
}

onMounted(async () => {
  const categoriesResponse = await fetch(`${marketplaceApiBaseUrl}/categories`)
  categories.value = await categoriesResponse.json()
  await loadApps()
})
</script>

<template>
  <section id="apps" class="showcase-section">
    <div class="container">
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

      <div class="showcase-toolbar">
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

        <div v-if="props.fullPage" class="search-bar">
          <i class="pi pi-search search-icon" />
          <input
            v-model="search"
            class="search-input"
            type="text"
            placeholder="Search by app name, summary, or tag"
            @keyup.enter="loadApps"
          />
          <button type="button" class="btn btn-primary search-button" @click="loadApps">
            Search
          </button>
        </div>
      </div>

      <div v-if="loading" class="showcase-empty">
        <div class="empty-orb" />
        <p>Loading marketplace apps...</p>
      </div>

      <div v-else-if="apps.length === 0" class="showcase-empty">
        <div class="empty-orb" />
        <p>No apps matched this category yet. Try another tab or search term.</p>
      </div>

      <div v-else class="apps-grid">
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
            <img v-if="app.thumbnailUrl" :src="app.thumbnailUrl" class="app-thumbnail" :alt="app.name" />
            <div v-else class="app-thumbnail app-thumbnail-fallback">
              <span>{{ app.name.charAt(0) }}</span>
            </div>
            <div class="app-overlay">
              <span v-if="app.isFeatured" class="overlay-badge">Featured</span>
              <span class="overlay-link">
                View details
                <i class="pi pi-arrow-up-right" />
              </span>
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
              <p class="app-summary">{{ app.summary || app.description || 'Open the detail page to explore the app.' }}</p>
            </div>

            <div class="app-footer">
              <span class="app-open">
                View details
                <i class="pi pi-arrow-right" />
              </span>
              <a
                v-if="getLaunchUrl(app.publicUrl)"
                class="app-launch"
                :href="getLaunchUrl(app.publicUrl) || undefined"
                target="_blank"
                rel="noopener"
                @click.stop
              >
                Launch
              </a>
            </div>
          </div>
        </article>
      </div>
    </div>
  </section>
</template>

<style scoped>
.showcase-section {
  padding: 88px 0 108px;
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
  display: grid;
  gap: 20px;
  margin-bottom: 28px;
}

.category-tabs {
  display: flex;
  flex-wrap: wrap;
  gap: 10px;
}

.category-tab {
  padding: 10px 14px;
  border: 1px solid var(--color-border);
  border-radius: 999px;
  background: rgba(255, 255, 255, 0.03);
  color: var(--color-text-muted);
  font-size: 0.88rem;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.2s ease;
}

.category-tab:hover {
  color: var(--color-text);
  border-color: rgba(124, 106, 245, 0.35);
  background: rgba(124, 106, 245, 0.08);
}

.category-tab-active {
  color: var(--color-text);
  border-color: rgba(124, 106, 245, 0.45);
  background: linear-gradient(135deg, rgba(124, 106, 245, 0.22), rgba(77, 166, 255, 0.12));
  box-shadow: inset 0 0 0 1px rgba(124, 106, 245, 0.2);
}

.search-bar {
  display: grid;
  grid-template-columns: auto minmax(0, 1fr) auto;
  align-items: center;
  gap: 12px;
  padding: 12px 14px;
  border-radius: 20px;
  border: 1px solid var(--color-border);
  background:
    linear-gradient(135deg, rgba(124, 106, 245, 0.08), rgba(77, 166, 255, 0.04)),
    var(--color-surface);
}

.search-icon {
  color: var(--color-text-muted);
}

.search-input {
  min-width: 0;
  background: transparent;
  border: none;
  color: var(--color-text);
  font-size: 0.96rem;
  outline: none;
}

.search-input::placeholder {
  color: var(--color-text-muted);
}

.search-button {
  white-space: nowrap;
}

.apps-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(260px, 1fr));
  gap: 20px;
}

.app-card {
  display: grid;
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
  aspect-ratio: 16 / 10;
  overflow: hidden;
  background: linear-gradient(135deg, rgba(124, 106, 245, 0.24), rgba(77, 166, 255, 0.18));
}

.app-thumbnail {
  width: 100%;
  height: 100%;
  object-fit: cover;
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

.overlay-link {
  align-self: flex-end;
  background: rgba(10, 10, 15, 0.66);
  color: rgba(255, 255, 255, 0.9);
  border: 1px solid rgba(255, 255, 255, 0.12);
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

.app-summary {
  color: var(--color-text-muted);
  font-size: 0.92rem;
  display: -webkit-box;
  -webkit-line-clamp: 3;
  -webkit-box-orient: vertical;
  overflow: hidden;
}

.app-footer {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}

.app-open {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  color: var(--color-text);
  font-size: 0.88rem;
  font-weight: 600;
}

.app-launch {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  padding: 8px 12px;
  border-radius: 999px;
  border: 1px solid rgba(124, 106, 245, 0.28);
  color: var(--color-accent-hover);
  font-size: 0.82rem;
  font-weight: 700;
  transition: background 0.2s ease, border-color 0.2s ease;
}

.app-launch:hover {
  background: rgba(124, 106, 245, 0.12);
  border-color: rgba(124, 106, 245, 0.48);
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
  .showcase-section {
    padding: 72px 0 88px;
  }

  .search-bar {
    grid-template-columns: 1fr;
  }

  .search-icon {
    display: none;
  }

  .search-button {
    width: 100%;
    justify-content: center;
  }

  .apps-grid {
    grid-template-columns: 1fr;
  }
}
</style>
