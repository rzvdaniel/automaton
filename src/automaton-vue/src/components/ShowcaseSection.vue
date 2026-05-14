<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'

const props = defineProps<{ fullPage?: boolean }>()

type MarketplaceApp = { id: string; name: string; summary?: string; thumbnailUrl?: string; category?: string; tags?: string; viewCount: number; starCount: number; isStarredByCurrentUser?: boolean; publicUrl?: string }

const categories = ref<string[]>([])
const apps = ref<MarketplaceApp[]>([])
const activeCategory = ref('')
const search = ref('')

const filteredApps = computed(() => apps.value)

const loadApps = async () => {
  const query = new URLSearchParams()
  if (activeCategory.value) query.set('category', activeCategory.value)
  if (search.value) query.set('search', search.value)
  query.set('take', props.fullPage ? '100' : '8')
  const res = await fetch(`/api/public/marketplace/apps?${query.toString()}`)
  apps.value = await res.json()
}

onMounted(async () => {
  const categoriesRes = await fetch('/api/public/marketplace/categories')
  categories.value = await categoriesRes.json()
  activeCategory.value = categories.value[0] ?? ''
  await loadApps()
})
</script>

<template>
  <section id="apps" class="showcase-section">
    <div class="container">
      <h2>Explore apps built with Automaton</h2>
      <div style="display:flex; gap:8px; margin: 12px 0; flex-wrap:wrap;">
        <button v-for="cat in categories" :key="cat" class="cat-tab" :class="{active: activeCategory===cat}" @click="activeCategory=cat; loadApps()">{{ cat }}</button>
      </div>
      <div v-if="props.fullPage" style="margin-bottom:12px;">
        <input v-model="search" placeholder="Search apps..." @keyup.enter="loadApps" />
        <button class="cat-tab active" @click="loadApps">Search</button>
      </div>
      <div class="apps-grid">
        <article v-for="app in filteredApps" :key="app.id" class="app-card">
          <img v-if="app.thumbnailUrl" :src="app.thumbnailUrl" class="app-thumbnail" :alt="app.name" />
          <h3>{{ app.name }}</h3>
          <p>{{ app.summary }}</p>
          <p>⭐ Stars: {{ app.starCount }} · 👁 Views: {{ app.viewCount }}</p>
          <a v-if="app.publicUrl" :href="`/web/${app.publicUrl}`" target="_blank">Open app</a>
        </article>
      </div>
    </div>
  </section>
</template>

<style scoped>
.apps-grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(220px,1fr));gap:16px}.app-card{border:1px solid #333;padding:12px;border-radius:10px}.cat-tab{padding:6px 12px}.active{background:#6b5cff;color:white}.app-thumbnail{width:100%;height:140px;object-fit:cover;border-radius:8px}
</style>
