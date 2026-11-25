<script setup lang="ts">
import { RouterLink } from 'vue-router'
import BHeader from '@/components/site/b/BHeader.vue'
import BFooter from '@/components/site/b/BFooter.vue'
import BFinal from '@/components/site/b/BFinal.vue'
import { publicAppConfig } from '@/config/publicAppConfig'
import { useSiteTheme } from '@/composables/useSiteTheme'
import '@/assets/site-b.css'
import { computed, onMounted, ref } from 'vue'
import { matchesSearch, useCatalog } from '@/composables/useCatalog'

const { apps, loading, error, load } = useCatalog()
const category = ref('all')
const search = ref('')
const categories = computed(() => [...new Set(apps.value.map((a) => a.category))].sort())
const recommended = computed(() => apps.value.filter((a) => a.recommended).sort((a, b) => a.recommendedOrder - b.recommendedOrder))
const filtering = computed(() => search.value.trim() !== '' || category.value !== 'all')
const visibleApps = computed(() => apps.value.filter((a) => (category.value === 'all' || a.category === category.value) && matchesSearch(a, search.value)))
onMounted(() => void load())
useSiteTheme()
</script>

<template>
  <div class="site b">
    <BHeader />
    <main>
<header class="hero"><div class="wrap"><div>
  <p class="eyebrow">Catalog</p>
  <h1>Find an app to install. <em>No server needed to look.</em></h1>
  <p class="lede">Every app in the official catalog, with what it does, the permissions it asks for and its licence. Pick one, and install it on your own server when you are ready.</p>
  <div class="row"><RouterLink class="btn" to="/how-catalogs-work">Publish your own catalog</RouterLink></div>
</div></div></header>
<section class="band"><div class="wrap">
  <div class="head"><label class="eyebrow" for="catalog-search">Search</label>
    <input id="catalog-search" v-model="search" type="search" placeholder="Search apps, e.g. database" autocomplete="off" style="display:block;width:100%;max-width:420px;padding:10px 14px;border-radius:10px;border:1px solid var(--line, #ccc);font:inherit" /></div>
  <div v-if="categories.length > 1" class="filters" role="group" aria-label="Category">
    <button v-for="cat in ['all', ...categories]" :key="cat" class="tab" type="button" :aria-pressed="cat === category" @click="category = cat">{{ cat === 'all' ? 'All' : cat }}</button>
  </div>
  <p v-if="loading" class="soft">Reading the catalog from GitHub…</p>
  <p v-else-if="error" class="soft">The catalog could not be read right now. <a :href="publicAppConfig.exampleAppsUrl" target="_blank" rel="noopener">See it on GitHub</a>.</p>
  <p v-else-if="apps.length === 0" class="soft">The catalog is empty right now.</p>
  <template v-else>
    <template v-if="!filtering && recommended.length">
      <h2 class="subh">Recommended</h2>
      <ul class="clist">
        <li v-for="app in recommended" :key="app.id" class="citem">
          <img v-if="app.icon" class="kic" :src="app.icon" alt="" width="44" height="44" loading="lazy" />
          <span v-else class="kic tool" aria-hidden="true">{{ app.initials }}</span>
          <div><h3><RouterLink :to="`/catalog/${app.id}`">{{ app.name }}</RouterLink></h3><p>{{ app.summary }}</p></div>
        </li>
      </ul>
      <h2 class="subh">All apps</h2>
    </template>
    <p v-if="visibleApps.length === 0" class="soft">No app matches that search.</p>
    <ul v-else class="clist">
      <li v-for="app in visibleApps" :key="app.id" class="citem">
        <img v-if="app.icon" class="kic" :src="app.icon" alt="" width="44" height="44" loading="lazy" />
        <span v-else class="kic tool" aria-hidden="true">{{ app.initials }}</span>
        <div><h3><RouterLink :to="`/catalog/${app.id}`">{{ app.name }}</RouterLink></h3><p>{{ app.summary }}</p>
          <div class="chips"><span>{{ app.category }}</span><span v-for="p in app.permissions" :key="p">asks for {{ p }}</span></div></div>
        <div class="acts"><RouterLink class="btn primary" :to="`/catalog/${app.id}`">View</RouterLink></div>
      </li>
    </ul>
  </template>
  <p class="soft" style="margin-top:14px;font-size:14px">Read live from the repository's <code>catalog.json</code>.</p>
</div></section>
    </main>
    <BFinal />
    <BFooter />
  </div>
</template>
