<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import MarketplaceAppRow from '@/components/MarketplaceAppRow.vue'
import StoreFooter from '@/components/StoreFooter.vue'
import StoreHeader from '@/components/StoreHeader.vue'
import { publicAppConfig } from '@/config/publicAppConfig'
import { useSiteTheme } from '@/composables/useSiteTheme'
import type { MarketplaceAppCard, PublicAppsResponse, PublicUserProfile } from '@/types/marketplace'

const route = useRoute()

const profile = ref<PublicUserProfile | null>(null)
const apps = ref<MarketplaceAppCard[]>([])
const loading = ref(true)
const errorMessage = ref('')
const activeCategory = ref('')

useSiteTheme()

const publicUsername = computed(() => String(route.params.publicUsername || ''))
const displayName = computed(() => profile.value?.displayName || profile.value?.username || publicUsername.value)
const categories = computed(() => Array.from(new Set(apps.value.map((app) => app.category).filter(Boolean) as string[])))
const visibleApps = computed(() => apps.value.filter((app) => !activeCategory.value || app.category === activeCategory.value))

async function loadCreatorApps() {
  loading.value = true
  errorMessage.value = ''
  activeCategory.value = ''
  try {
    const response = await fetch(`${publicAppConfig.apiBaseUrl}api/public/${encodeURIComponent(publicUsername.value)}/apps`)
    if (!response.ok) {
      errorMessage.value = response.status === 404 ? 'This creator was not found.' : 'Unable to load this creator store.'
      profile.value = null
      apps.value = []
      return
    }
    const data = (await response.json()) as PublicAppsResponse
    profile.value = data.profile
    apps.value = data.apps
  } catch {
    errorMessage.value = 'Unable to load this creator store.'
    profile.value = null
    apps.value = []
  } finally {
    loading.value = false
  }
}

watch(publicUsername, loadCreatorApps)
onMounted(loadCreatorApps)
</script>

<template>
  <div class="site creator-page">
    <StoreHeader />
    <main>
      <section class="band intro">
        <div class="wrap">
          <RouterLink class="back" to="/"><span aria-hidden="true">←</span> All public apps</RouterLink>
          <h1>{{ displayName }}</h1>
          <p class="lede">{{ profile?.bio || 'Public apps by this creator.' }}</p>
        </div>
      </section>

      <section class="band results">
        <div class="wrap">
          <p v-if="loading" class="state">Loading apps…</p>
          <div v-else-if="errorMessage" class="state">
            <p>{{ errorMessage }}</p>
            <RouterLink class="btn small" to="/">Browse public apps</RouterLink>
          </div>
          <template v-else>
            <nav v-if="categories.length > 1" class="chips" aria-label="Categories">
              <button class="chip" :class="{ active: !activeCategory }" type="button" @click="activeCategory = ''">All</button>
              <button
                v-for="category in categories"
                :key="category"
                class="chip"
                :class="{ active: activeCategory === category }"
                type="button"
                @click="activeCategory = category"
              >{{ category }}</button>
            </nav>
            <p v-if="apps.length === 0" class="state">This creator has no public apps yet.</p>
            <ul v-else class="app-grid">
              <li v-for="app in visibleApps" :key="app.id"><MarketplaceAppRow :app="app" /></li>
            </ul>
          </template>
        </div>
      </section>
    </main>
    <StoreFooter />
  </div>
</template>

<style scoped>
.intro .wrap {
  padding-block: 20px 12px;
  display: grid;
  gap: 4px;
  justify-items: start;
}
.intro h1 {
  font-size: clamp(1.8rem, 4vw, 2.4rem);
  font-weight: 800;
  overflow-wrap: anywhere;
}
.site .back {
  color: var(--ink-soft);
  text-decoration: none;
}
.results .wrap {
  padding-block: 16px 56px;
  display: grid;
  gap: 14px;
}
.chips {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}
.chip {
  font: 500 0.82rem var(--body);
  padding: 5px 12px;
  border-radius: 999px;
  border: 1px solid var(--line);
  color: var(--ink-soft);
  background: var(--card);
  cursor: pointer;
}
.chip:hover {
  color: var(--ink);
  border-color: var(--ink);
}
.chip.active {
  background: var(--ink);
  border-color: var(--ink);
  color: var(--paper);
}
.app-grid {
  list-style: none;
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 10px;
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
@media (max-width: 900px) {
  .app-grid {
    grid-template-columns: minmax(0, 1fr);
  }
}
</style>
