<script setup lang="ts">
import { computed, onMounted, ref, watchEffect } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import BHeader from '@/components/site/b/BHeader.vue'
import BFooter from '@/components/site/b/BFooter.vue'
import { publicAppConfig } from '@/config/publicAppConfig'
import { useSiteTheme } from '@/composables/useSiteTheme'
import { applySeo } from '@/seo/useSeo'
import '@/assets/site-b.css'
import { normalizeStudioAddress, studioInstallUrl, useCatalog } from '@/composables/useCatalog'

const STUDIO_KEY = 'automaton.studioAddress'
const route = useRoute()
const { apps, loading, error, load } = useCatalog()
const app = computed(() => apps.value.find((a) => a.id === String(route.params.id)))
const asking = ref(false)
const studio = ref('')
const invalid = ref(false)

onMounted(() => {
  void load()
  try { studio.value = localStorage.getItem(STUDIO_KEY) ?? '' } catch { /* browser storage unavailable */ }
})
watchEffect(() => {
  if (typeof window === 'undefined' || !app.value) return
  applySeo({ title: `Self-host ${app.value.name} in 3 clicks`, description: app.value.summary, path: route.path })
})

function install() {
  const target = app.value
  const origin = normalizeStudioAddress(studio.value)
  if (!target || !origin) { invalid.value = true; return }
  invalid.value = false
  try { localStorage.setItem(STUDIO_KEY, origin) } catch { /* not remembered */ }
  window.location.assign(studioInstallUrl(origin, target))
}
function forget() {
  try { localStorage.removeItem(STUDIO_KEY) } catch { /* ignore */ }
  studio.value = ''
}
useSiteTheme()
</script>

<template>
  <div class="site b">
    <BHeader />
    <main>
<section class="band"><div class="wrap">
  <p class="soft"><RouterLink to="/catalog">← Catalog</RouterLink></p>
  <p v-if="loading" class="soft">Reading the catalog from GitHub…</p>
  <p v-else-if="error" class="soft">The catalog could not be read right now. <a :href="publicAppConfig.exampleAppsUrl" target="_blank" rel="noopener">See it on GitHub</a>.</p>
  <p v-else-if="!app" class="soft">This app is not in the catalog. <RouterLink to="/catalog">Browse the catalog</RouterLink>.</p>
  <template v-else>
    <div class="citem">
      <img v-if="app.icon" class="kic" :src="app.icon" alt="" width="44" height="44" />
      <span v-else class="kic tool" aria-hidden="true">{{ app.initials }}</span>
      <div><h1>Self-host {{ app.name }} in 3 clicks</h1><p class="lede">{{ app.summary }}</p>
        <div class="chips"><span>{{ app.category }}</span><span v-if="app.minServerVersion">server {{ app.minServerVersion }}+</span></div></div>
    </div>
    <div class="row" style="margin:18px 0">
      <button v-if="!asking" class="btn primary" type="button" @click="asking = true">Install on your server</button>
      <RouterLink class="btn" to="/get-started">I don't have a server yet</RouterLink>
    </div>
    <form v-if="asking" class="soft" style="margin-bottom:24px" @submit.prevent="install">
      <label for="studio-address"><b>Your Studio address</b></label><br />
      <input id="studio-address" v-model="studio" type="text" inputmode="url" placeholder="studio.example.com" autocomplete="url" style="padding:10px 14px;border-radius:10px;border:1px solid var(--line, #ccc);font:inherit;min-width:280px" />
      <button class="btn primary" type="submit">Open the install screen</button>
      <p v-if="invalid">Enter your Studio's web address, for example studio.example.com.</p>
      <p style="font-size:14px">Remembered in this browser only. <button v-if="studio" class="tab" type="button" @click="forget">Forget it</button></p>
    </form>
    <div v-if="app.screenshots.length" style="display:flex;gap:12px;overflow-x:auto;margin-bottom:24px">
      <img v-for="(shot, i) in app.screenshots" :key="shot" :src="shot" :alt="`${app.name} screenshot ${i + 1}`" loading="lazy" style="height:240px;border-radius:10px;border:1px solid var(--line, #ccc)" />
    </div>
    <h2 class="subh">Permissions</h2>
    <p v-if="!app.permissions.length" class="soft">This app asks for no permissions.</p>
    <div v-else class="chips"><span v-for="p in app.permissions" :key="p">asks for {{ p }}</span></div>
    <h2 class="subh">Licence</h2>
    <p class="soft">{{ app.license || 'Not stated in the catalog; read the source for the licence.' }} · <a :href="app.sourceUrl" target="_blank" rel="noopener">Source on GitHub</a></p>
  </template>
</div></section>
    </main>
    <BFooter />
  </div>
</template>
