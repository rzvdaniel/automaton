<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { resolveMarketplaceAssetUrl, useMarketplaceApps } from '@/composables/useMarketplaceApps'

const router = useRouter()
const route = useRoute()
const searchText = ref('')
const searchOpen = ref(false)
const { apps, loadApps } = useMarketplaceApps(60)

const categories = ['Featured', 'Games', 'AI Tools', 'Backend Apps', 'Most Forked']

const activeCategory = computed(() => String(route.query.category || 'Featured'))

function buildAppsQuery(category = activeCategory.value) {
  const query: Record<string, string> = {}

  if (category && category !== 'Featured') {
    query.category = category
  }

  return query
}

function goToApps(category = activeCategory.value) {
  void router.push({ path: '/apps', query: buildAppsQuery(category) })
}

const matchedApps = computed(() => {
  const query = searchText.value.trim().toLowerCase()
  if (!query) return []

  return apps.value
    .filter((app) => {
      const haystack = [
        app.name,
        app.summary ?? '',
        app.description ?? '',
        app.category ?? '',
        app.creatorDisplayName ?? '',
        app.creatorPublicUsername ?? '',
      ]
        .join(' ')
        .toLowerCase()

      return haystack.includes(query)
    })
    .slice(0, 5)
})

function openApp(appId: string) {
  searchOpen.value = false
  searchText.value = ''
  void router.push(`/apps/${appId}`)
}

function handleSearchFocus() {
  searchOpen.value = true
}

function handleSearchBlur() {
  window.setTimeout(() => {
    searchOpen.value = false
  }, 120)
}

function selectCategory(category: string) {
  goToApps(category)
}

onMounted(() => {
  void loadApps(60)
})
</script>

<template>
  <div class="discovery-strip">
    <div class="shell strip-inner">
      <div class="category-tabs" role="tablist" aria-label="App discovery filters">
        <button
          v-for="category in categories"
          :key="category"
          class="category-tab"
          :class="{ active: activeCategory === category }"
          type="button"
          @click="selectCategory(category)"
        >
          {{ category }}
        </button>
      </div>

      <div class="search-wrap">
        <label class="search-bar">
          <i class="pi pi-search search-icon" />
          <input
            v-model="searchText"
            class="search-input"
            type="search"
            placeholder="Search apps..."
            @focus="handleSearchFocus"
            @blur="handleSearchBlur"
          />
        </label>

        <div v-if="searchOpen && searchText.trim()" class="search-menu">
          <button
            v-for="app in matchedApps"
            :key="app.id"
            class="search-result"
            type="button"
            @mousedown.prevent="openApp(app.id)"
          >
            <img
              v-if="app.thumbnailUrl"
              :src="resolveMarketplaceAssetUrl(app.thumbnailUrl)"
              :alt="app.name"
              class="search-result-image"
            />
            <div v-else class="search-result-fallback">{{ app.name.charAt(0) }}</div>
            <div class="search-result-copy">
              <strong>{{ app.name }}</strong>
              <span>{{ app.category || 'App' }}</span>
            </div>
          </button>

          <div v-if="matchedApps.length === 0" class="search-empty">No matching apps.</div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.discovery-strip {
  position: sticky;
  top: 68px;
  z-index: 110;
  border-bottom: 1px solid rgba(255, 255, 255, 0.08);
  background:
    linear-gradient(135deg, rgba(80, 215, 255, 0.055), rgba(69, 230, 138, 0.035)),
    rgba(7, 8, 11, 0.92);
  backdrop-filter: blur(18px);
}

.shell {
  width: min(1260px, calc(100% - 40px));
  margin: 0 auto;
}

.strip-inner {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 14px;
  padding: 10px 0 12px;
}

.category-tabs {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  min-width: 0;
}

.search-wrap {
  position: relative;
  width: min(320px, 100%);
  flex: 0 0 auto;
}

.search-bar {
  display: flex;
  align-items: center;
  gap: 8px;
  width: 100%;
  min-width: 0;
  padding: 7px 10px;
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 8px;
  background: rgba(255, 255, 255, 0.045);
  color: #50d7ff;
}

.search-icon {
  color: #50d7ff;
}

.search-input {
  width: 100%;
  min-width: 0;
  border: 0;
  outline: none;
  background: transparent;
  color: #f5f7fb;
  font-size: 0.86rem;
}

.search-input::placeholder {
  color: #9ba3b4;
}

.category-tab {
  padding: 8px 12px;
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 999px;
  background: rgba(255, 255, 255, 0.03);
  color: #9ba3b4;
  font-size: 0.88rem;
  font-weight: 850;
  white-space: nowrap;
  cursor: pointer;
}

.category-tab:hover {
  border-color: rgba(80, 215, 255, 0.28);
  color: #f5f7fb;
  background: rgba(80, 215, 255, 0.08);
}

.category-tab.active {
  border-color: rgba(69, 230, 138, 0.42);
  color: #45e68a;
  background: rgba(69, 230, 138, 0.14);
}

.search-menu {
  position: absolute;
  top: calc(100% + 8px);
  right: 0;
  left: 0;
  display: grid;
  overflow: hidden;
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 8px;
  background: rgba(10, 14, 20, 0.98);
  box-shadow: 0 20px 54px rgba(0, 0, 0, 0.34);
}

.search-result {
  display: grid;
  grid-template-columns: 52px 1fr;
  gap: 10px;
  align-items: center;
  width: 100%;
  padding: 10px;
  border: 0;
  border-bottom: 1px solid rgba(255, 255, 255, 0.06);
  background: transparent;
  color: #f5f7fb;
  text-align: left;
  cursor: pointer;
}

.search-result:last-child {
  border-bottom: 0;
}

.search-result:hover {
  background: rgba(255, 255, 255, 0.04);
}

.search-result-image,
.search-result-fallback {
  width: 52px;
  height: 36px;
  border-radius: 6px;
  object-fit: cover;
  background: #0b0f15;
}

.search-result-fallback {
  display: grid;
  place-items: center;
  color: #50d7ff;
  font-weight: 900;
}

.search-result-copy {
  display: grid;
  gap: 2px;
  min-width: 0;
}

.search-result-copy strong {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-size: 0.88rem;
}

.search-result-copy span,
.search-empty {
  color: #9ba3b4;
  font-size: 0.78rem;
}

.search-empty {
  padding: 12px 10px;
}

@media (max-width: 640px) {
  .shell {
    width: min(100% - 28px, 1180px);
  }

  .discovery-strip {
    top: 68px;
  }

  .strip-inner {
    display: grid;
  }

  .search-wrap {
    width: 100%;
  }
}
</style>
