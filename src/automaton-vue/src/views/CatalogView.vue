<script setup lang="ts">
import { RouterLink } from 'vue-router'
import BHeader from '@/components/site/b/BHeader.vue'
import BFooter from '@/components/site/b/BFooter.vue'
import BFinal from '@/components/site/b/BFinal.vue'
import { publicAppConfig } from '@/config/publicAppConfig'
import { useSiteTheme } from '@/composables/useSiteTheme'
import '@/assets/site-b.css'
import { computed, onMounted, ref } from 'vue'
import { useCatalog } from '@/composables/useCatalog'

const { apps, loading, error, load } = useCatalog()
const category = ref('all')
const categories = computed(() => [...new Set(apps.value.map((a) => a.category))].sort())
const visibleApps = computed(() => apps.value.filter((a) => category.value === 'all' || a.category === category.value))
onMounted(() => void load())
useSiteTheme()
</script>

<template>
  <div class="site b">
    <BHeader />
    <main>

<header class="hero"><div class="wrap">
  <div>
    <p class="eyebrow">Catalog</p>
    <h1>Apps as source code. <em>Install them when you need them.</em></h1>
    <p class="lede">The Automaton catalog is an open-source GitHub repository of apps, curated for quality. They wait as source until you install one; then your server builds and runs it. Anyone can publish a catalog like it: an app needs only an extra <code>automaton.json</code>.</p>
    <div class="row"><a class="btn primary" :href="publicAppConfig.exampleAppsUrl" target="_blank" rel="noopener">View on GitHub</a><a class="btn" href="#make-your-own">Make your own catalog</a></div>
    <div class="trust"><span>Open source, Apache 2.0</span><span>Built from source on your server</span><span>One click in Studio</span></div>
  </div>
  <svg class="dia" viewBox="0 0 540 300" role="img" aria-labelledby="cat-t"><title id="cat-t">Apps wait as source in GitHub repositories, appear in your Studio catalog, and are installed on your server when you choose; an installed app can be forked.</title>
      <rect class="box" x="10" y="20" width="150" height="56" rx="12"/><text class="t" x="24" y="44">team-tools</text><text class="s" x="24" y="62">github · 12 apps</text>
      <rect class="box" x="10" y="92" width="150" height="56" rx="12"/><text class="t" x="24" y="116">automaton-apps</text><text class="s" x="24" y="134">github · public</text>
      <rect class="box" x="10" y="164" width="150" height="56" rx="12"/><text class="t" x="24" y="188">company-private</text><text class="s" x="24" y="206">github · private</text>
      <text class="s" x="14" y="250">source, dormant</text>
      <path class="wire" d="M160 48H210M160 120H210M160 192H210M210 48V192M210 120H240"/>
      <rect class="hotb" x="240" y="80" width="130" height="80" rx="14"/><text class="t" x="256" y="112">Catalog</text><text class="s" x="256" y="132">in Studio</text>
      <path class="wire" d="M370 120H410"/><text class="s" x="374" y="110">install</text>
      <rect class="hub" x="410" y="80" width="120" height="80" rx="14"/><text class="hubt" x="424" y="112">Running</text><text class="hubs" x="424" y="132">your server</text><circle class="pulse" cx="512" cy="96" r="5"/>
      <path class="wire" d="M470 160V220"/><rect class="box" x="410" y="220" width="120" height="56" rx="12"/><text class="t" x="424" y="244">Your fork</text><text class="s" x="424" y="262">adapted by AI</text>
    </svg>
</div></header>
<section class="band alt"><div class="wrap">
  <div class="head"><p class="eyebrow">Catalog sources</p><h2>One server, many catalogs</h2><p>Every server starts with the official catalog that we curate. Most teams add their own next to it: a private repository for company tools, or a public one from someone they trust. Studio lists all of them together, and each entry shows which source it comes from.</p></div>
  <div class="srcs">
    <div class="src on"><span class="pill live">official</span><b>rzvdaniel/automaton-apps</b><span class="soft">Curated by the Automaton project · public · on by default, can be switched off</span></div>
    <div class="src"><span class="pill">private</span><b>your-company/internal-tools</b><span class="soft">Your team's own apps · read with a read-only token, stored encrypted</span></div>
    <div class="src"><span class="pill">public</span><b>someone/their-apps</b><span class="soft">Any GitHub repository with a catalog.json that you trust</span></div>
    <div class="src add"><b>+ Add a source</b><span class="soft">Studio → Apps → Catalog → Catalog sources, or ask your agent</span></div>
  </div>
  <h3 class="subh">In the official catalog <code>rzvdaniel/automaton-apps</code></h3>
  <p class="soft" style="margin-bottom:18px">Install any of these from <b>Apps → Catalog</b> in your Studio. Studio shows the source, the exact commit and the permissions before anything is created.</p>
  <div v-if="categories.length > 1" class="filters" role="group" aria-label="Category">
    <button v-for="cat in ['all', ...categories]" :key="cat" class="tab" type="button" :aria-pressed="cat === category" @click="category = cat">{{ cat === 'all' ? 'All' : cat }}</button>
  </div>
  <p v-if="loading" class="soft">Reading the catalog from GitHub…</p>
  <p v-else-if="error" class="soft">The catalog could not be read right now. <a :href="publicAppConfig.exampleAppsUrl" target="_blank" rel="noopener">See it on GitHub</a>.</p>
  <p v-else-if="apps.length === 0" class="soft">The catalog is empty right now.</p>
  <ul v-else class="clist">
    <li v-for="app in visibleApps" :key="app.id" class="citem">
      <span class="kic tool" aria-hidden="true">{{ app.initials }}</span>
      <div><h3>{{ app.name }} <code>{{ app.folder }}</code></h3><p>{{ app.summary }}</p>
        <div class="chips"><span>{{ app.category }}</span><span v-if="app.minServerVersion">server {{ app.minServerVersion }}+</span><span v-for="p in app.permissions" :key="p">asks for {{ p }}</span></div></div>
      <div class="acts"><a class="btn" :href="app.sourceUrl" target="_blank" rel="noopener">Source</a><RouterLink class="btn primary" to="/get-started">How to install</RouterLink></div>
    </li>
  </ul>
  <p class="soft" style="margin-top:14px;font-size:14px">Read live from the repository's <code>catalog.json</code>. Install from <b>Apps → Catalog</b> in your own Studio.</p>
</div></section>
<section id="make-your-own" class="band"><div class="wrap">
  <div class="feat" style="padding:0">
    <div><p class="eyebrow">Make your own</p><h3>Any GitHub repository can be a catalog</h3><p class="soft">Keep your team's tools, or packaged open-source apps, in one repository. Add it in Studio as a catalog source.</p>
      <ul><li>Create the repository from the template, or ask your agent to</li><li>Add each app in its own folder with an <code>automaton.json</code></li><li>A workflow regenerates <code>catalog.json</code> on every push</li><li>Add the repository under Apps → Catalog → Catalog sources</li></ul>
      <div class="chat" style="margin-top:16px"><b>You</b>Package this open-source app for our catalog and add the catalog to our server.</div></div>
    <pre class="code"><span class="c">// the whole contract for a simple web app</span>
{
  <span class="k">"name"</span>: <span class="v">"Space Invaders"</span>,
  <span class="k">"summary"</span>: <span class="v">"A fast arcade shooter in your browser."</span>,
  <span class="k">"category"</span>: <span class="v">"Games"</span>,
  <span class="k">"application"</span>: {
    <span class="k">"build"</span>: { <span class="k">"image"</span>: <span class="v">"node:22-bookworm-slim"</span> }
  }
}</pre>
  </div>
</div></section>
    </main>
    <BFinal />
    <BFooter />
  </div>
</template>
