<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import PublicSiteHeader from '@/components/PublicSiteHeader.vue'
import { publicAppConfig } from '@/config/publicAppConfig'
import { resolveMarketplaceAssetUrl, useMarketplaceApps } from '@/composables/useMarketplaceApps'
import type { MarketplaceAppCard } from '@/types/marketplace'
import pacManImage from '@/assets/images/pac-man.png'
import snakeImage from '@/assets/images/snake.png'
import tetrisImage from '@/assets/images/tetris.png'
import flappyBirdsImage from '@/assets/images/flappy-birds.png'

type FallbackApp = {
  id: string
  name: string
  creatorDisplayName: string
  category: string
  summary?: string
  description: string
  thumbnailUrl: string
  screenshots: { id: string; imageUrl: string; caption: string; sortOrder: number }[]
  starCount: number
  viewCount: number
}

const router = useRouter()
const { apps, featuredApps, loadApps } = useMarketplaceApps(12)
const activeIndex = ref(0)
const activePreviewUrl = ref<string | null>(null)
const showCopyNotice = ref(false)

const fallbackApps: FallbackApp[] = [
  {
    id: 'fallback-pac-man',
    name: 'Pac-Man Editor and Game',
    creatorDisplayName: 'Razvan Predescu',
    category: 'Games',
    summary: 'Live apps become templates, remixes, and proof that one person can ship useful software with an AI agent.',
    description: 'Live apps become templates, remixes, and proof that one person can ship useful software with an AI agent.',
    thumbnailUrl: pacManImage,
    screenshots: [
      { id: 'snake', imageUrl: snakeImage, caption: 'Snake app screenshot', sortOrder: 1 },
      { id: 'tetris', imageUrl: tetrisImage, caption: 'Tetris app screenshot', sortOrder: 2 },
      { id: 'flappy', imageUrl: flappyBirdsImage, caption: 'Flappy Birds app screenshot', sortOrder: 3 },
    ],
    starCount: 0,
    viewCount: 55,
  },
]

const heroApps = computed<(MarketplaceAppCard | FallbackApp)[]>(() => {
  const realApps = featuredApps.value.length > 0 ? featuredApps.value : apps.value
  return realApps.length > 0 ? realApps.slice(0, 8) : fallbackApps
})

const activeApp = computed(() => heroApps.value[activeIndex.value] ?? heroApps.value[0])
const activeMainImageUrl = computed(() => activePreviewUrl.value || activeApp.value?.thumbnailUrl || '')
const activeAppCopy = computed(() => {
  const app = activeApp.value
  if (!app) return ''

  return app.summary || app.description || 'Live apps become templates, remixes, and proof that one person can ship useful software with an AI agent.'
})
const activeScreenshots = computed(() => {
  const app = activeApp.value
  if (!app) return []

  const screenshots = app.screenshots?.slice(0, 3) ?? []
  if (screenshots.length > 0) return screenshots

  return app.thumbnailUrl
    ? [{ id: 'thumbnail', imageUrl: app.thumbnailUrl, caption: app.name, sortOrder: 0 }]
    : []
})

function appImageUrl(url?: string | null) {
  if (!url) return ''
  if (url.startsWith('data:') || url.startsWith('blob:')) return url
  if (url.startsWith('http')) return url
  if (url.startsWith('/src/') || url.startsWith('/assets/')) return url
  return resolveMarketplaceAssetUrl(url)
}

function previousApp() {
  activeIndex.value = (activeIndex.value - 1 + heroApps.value.length) % heroApps.value.length
  activePreviewUrl.value = null
}

function nextApp() {
  activeIndex.value = (activeIndex.value + 1) % heroApps.value.length
  activePreviewUrl.value = null
}

function selectApp(index: number) {
  activeIndex.value = index
  activePreviewUrl.value = null
}

function previewScreenshot(url: string) {
  activePreviewUrl.value = url
}

function resetPreview() {
  activePreviewUrl.value = null
}

function openActiveApp() {
  const app = activeApp.value
  if (!app || app.id.startsWith('fallback-')) return
  void router.push(`/apps/${app.id}`)
}

async function copySetupFlow() {
  try {
    await navigator.clipboard.writeText('dotnet tool install -g Automaton.Cli\nautomaton setup')
    showCopyNotice.value = true
    window.setTimeout(() => {
      showCopyNotice.value = false
    }, 1500)
  } catch {
    showCopyNotice.value = false
  }
}

onMounted(() => {
  void loadApps()
})
</script>

<template>
  <div class="home-v2">
    <PublicSiteHeader />

    <main>
      <header class="hero">
        <div class="shell hero-grid">
          <div>
            <span class="eyebrow">AI-built apps, deployed by CLI</span>
            <h1>
              <span>AI can code.</span>
              <span>Automaton</span>
              <span>helps it ship.</span>
            </h1>
            <p class="hero-copy">
              Install once, run setup, then ask your AI coding agent for the app you want. Automaton Monster gives it the local instructions and CLI path to build, deploy, and remix real software.
            </p>
            <div class="prompt-strip" aria-label="Example AI prompt">
              <div class="prompt-label">PROMPT</div>
              <div class="prompt-text">Build a poker planning app for Automaton Monster.</div>
            </div>
            <div class="hero-actions">
              <RouterLink class="button primary" to="/cli">Install the CLI</RouterLink>
              <RouterLink class="button" to="/apps">Browse public apps</RouterLink>
            </div>
          </div>

          <div class="hero-showcase">
            <div class="carousel-frame" @mouseleave="resetPreview">
              <div class="carousel-label">
                <span>Featured & recommended</span>
                <span class="carousel-header-controls">
                  <button v-if="heroApps.length > 1" type="button" aria-label="Previous app" @click="previousApp">
                    <i class="pi pi-chevron-left" />
                  </button>
                  <span class="carousel-slide-count">{{ String(activeIndex + 1).padStart(2, '0') }} / {{ String(heroApps.length).padStart(2, '0') }}</span>
                  <button v-if="heroApps.length > 1" type="button" aria-label="Next app" @click="nextApp">
                    <i class="pi pi-chevron-right" />
                  </button>
                </span>
              </div>

              <article v-if="activeApp" class="featured-app" role="button" tabindex="0" @click="openActiveApp" @keydown.enter.prevent="openActiveApp">
                <div class="featured-image">
                  <img
                    :src="appImageUrl(activeMainImageUrl)"
                    :alt="`${activeApp.name} screenshot`"
                    @focus="resetPreview"
                    @mouseenter="resetPreview"
                  />
                </div>
                <div class="featured-detail">
                  <div>
                    <h2>{{ activeApp.name }}</h2>
                    <p v-if="activeApp.creatorDisplayName" class="creator-line">by {{ activeApp.creatorDisplayName }}</p>
                    <p class="featured-description">{{ activeAppCopy }}</p>
                  </div>
                  <div class="featured-thumbs">
                    <button
                      v-for="screenshot in activeScreenshots"
                      :key="screenshot.id"
                      class="thumb-button"
                      type="button"
                      :aria-label="screenshot.caption || activeApp.name"
                      @focus="previewScreenshot(screenshot.imageUrl)"
                      @mouseenter="previewScreenshot(screenshot.imageUrl)"
                    >
                      <img :src="appImageUrl(screenshot.imageUrl)" :alt="screenshot.caption || activeApp.name" />
                    </button>
                  </div>
                  <div class="carousel-controls">
                    <span class="carousel-bottom-controls">
                      <span class="carousel-dots">
                        <button
                          v-for="(_app, index) in heroApps"
                          :key="`${_app.id}-${index}`"
                          class="dot"
                          :class="{ active: index === activeIndex }"
                          type="button"
                          :aria-label="`Show app ${index + 1}`"
                          @click.stop="selectApp(index)"
                        />
                      </span>
                    </span>
                  </div>
                </div>
              </article>
            </div>

            <div id="install" class="quick-setup">
              <div>$ dotnet tool install -g Automaton.Cli</div>
              <div>$ automaton setup</div>
              <div><span class="muted"># Then ask:</span> Build a poker planning app for Automaton Monster.</div>
            </div>
            <div class="live-strip">
              <div class="live-item"><strong>42</strong>apps shipped</div>
              <div class="live-item"><strong>18</strong>templates</div>
              <div class="live-item"><strong>126</strong>remixes</div>
              <div class="live-item"><strong>self-host</strong>runner ready</div>
            </div>
          </div>
        </div>
      </header>

      <section id="build">
        <div class="shell">
          <div class="section-head">
            <h2>Install once. Build by asking.</h2>
            <p>The setup step gives your AI agent the Automaton guidance it needs. After that, you describe the app, not the deployment process.</p>
          </div>
          <div class="steps">
            <article class="step">
              <div class="step-number">1</div>
              <h3>Install the CLI</h3>
              <p>One global tool gives the AI a stable execution path instead of asking it to guess how Automaton works.</p>
            </article>
            <article class="step">
              <div class="step-number">2</div>
              <h3>Run setup once</h3>
              <p>Setup handles auth, creates AGENTS.md, writes .automaton instructions, and gives the current folder enough context.</p>
            </article>
            <article class="step">
              <div class="step-number">3</div>
              <h3>Ask for the app</h3>
              <p>The human prompt becomes simple: describe the product. The agent reads the local instructions and uses the CLI.</p>
            </article>
          </div>
        </div>
      </section>

      <section id="creators">
        <div class="shell feature-band">
          <div class="big-card">
            <h2>A living ecosystem of apps and remixes.</h2>
            <p>
              Every app has a builder behind it. Profiles, app collections, updates, remixes, and verified templates turn Automaton Monster from a deploy tool into a creation ecosystem.
            </p>
            <div class="stat-row">
              <div class="stat"><strong>CLI</strong><span>agent-native setup</span></div>
              <div class="stat"><strong>Fork</strong><span>start from working apps</span></div>
              <div class="stat"><strong>Proof</strong><span>runs, remixes, updates</span></div>
            </div>
          </div>
          <div class="creator-panel">
            <div class="creator-top">
              <div>
                <h3>Live ecosystem feed</h3>
                <p>Turn new builds into fresh marketplace activity.</p>
              </div>
              <span class="status">Live</span>
            </div>
            <div class="build-feed">
              <div class="feed-item"><code>10:42</code><span>Maria published "Support Macro Builder"</span><span class="status">New</span></div>
              <div class="feed-item"><code>10:28</code><span>Jon remixed "Sprint Poker" for mobile standups</span><span class="status">Remix</span></div>
              <div class="feed-item"><code>09:51</code><span>Automaton verified "Invoice Triage" screenshots</span><span class="status">Trusted</span></div>
              <div class="feed-item"><code>09:13</code><span>Team Ops shipped a public changelog update</span><span class="status">Updated</span></div>
            </div>
          </div>
        </div>
      </section>

      <section class="cta">
        <div class="shell cta-box">
          <div class="cta-copy">
            <h2>Build and publish your first app in minutes.</h2>
            <p>Install the setup flow, tell your AI what to build, then deploy directly to Automaton Monster.</p>
            <div class="cta-command-row">
              <div class="cta-command-preview">
                <div>$ dotnet tool install -g Automaton.Cli</div>
                <div>$ automaton setup</div>
              </div>
              <div class="cta-actions">
                <button class="button primary" type="button" @click="copySetupFlow">
                  Copy setup flow
                </button>
                <div v-if="showCopyNotice" class="copy-notice" role="status" aria-live="polite">
                  Setup commands copied to clipboard
                </div>
              </div>
            </div>
          </div>
        </div>
      </section>
    </main>

    <footer>
      <div class="shell footer-inner">
        <span>Automaton Monster</span>
        <span>CLI setup · AI app builds · public app discovery</span>
        <a :href="publicAppConfig.studioUrl" target="_blank" rel="noopener">Open Studio</a>
      </div>
    </footer>
  </div>
</template>

<style scoped>
.home-v2 {
  min-height: 100vh;
  background:
    radial-gradient(circle at 24% 0%, rgba(80, 215, 255, 0.12), transparent 30rem),
    radial-gradient(circle at 86% 10%, rgba(69, 230, 138, 0.1), transparent 28rem),
    linear-gradient(180deg, #090b10 0%, #07080b 44%, #050608 100%);
  color: #f5f7fb;
  font-family: Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
  line-height: 1.5;
  --v2-bg: #07080b;
  --v2-panel-3: #20242f;
  --v2-line: rgba(255, 255, 255, 0.1);
  --v2-text: #f5f7fb;
  --v2-muted: #9ba3b4;
  --v2-soft: #c8ceda;
  --v2-green: #45e68a;
  --v2-green-soft: rgba(69, 230, 138, 0.14);
  --v2-cyan: #50d7ff;
  --v2-cyan-soft: rgba(80, 215, 255, 0.12);
  --v2-shadow: 0 24px 80px rgba(0, 0, 0, 0.36);
}

.home-v2 *,
.home-v2 *::before,
.home-v2 *::after {
  box-sizing: border-box;
}

.home-v2 a {
  color: inherit;
  text-decoration: none;
}

.shell {
  width: min(1260px, calc(100% - 40px));
  margin: 0 auto;
}

.v2-nav {
  position: sticky;
  top: 0;
  z-index: 10;
  border-bottom: 1px solid var(--v2-line);
  background: rgba(7, 8, 11, 0.78);
  backdrop-filter: blur(18px);
}

.nav-inner,
.brand,
.nav-links,
.nav-actions,
.button,
.eyebrow,
.carousel-label,
.carousel-controls,
.pills,
.meta,
.creator-top,
.footer-inner {
  display: flex;
  align-items: center;
}

.nav-inner {
  justify-content: space-between;
  min-height: 68px;
  gap: 24px;
}

.brand {
  gap: 12px;
  font-weight: 800;
  letter-spacing: -0.02em;
}

.mark {
  display: grid;
  width: 34px;
  height: 34px;
  place-items: center;
  border: 1px solid rgba(69, 230, 138, 0.48);
  border-radius: 8px;
  background: linear-gradient(135deg, rgba(69, 230, 138, 0.18), rgba(80, 215, 255, 0.12));
  color: var(--v2-green);
  font-family: ui-monospace, SFMono-Regular, Consolas, monospace;
  box-shadow: 0 0 34px rgba(69, 230, 138, 0.14);
}

.nav-links {
  gap: 18px;
  color: var(--v2-muted);
  font-size: 0.94rem;
}

.nav-links a:hover {
  color: var(--v2-text);
}

.nav-actions {
  gap: 10px;
}

.button {
  justify-content: center;
  min-height: 40px;
  padding: 0 16px;
  border: 1px solid var(--v2-line);
  border-radius: 8px;
  background: rgba(255, 255, 255, 0.03);
  color: var(--v2-text);
  font-size: 0.92rem;
  font-weight: 700;
}

.button.primary {
  border-color: rgba(69, 230, 138, 0.58);
  background: var(--v2-green);
  color: #041008;
  box-shadow: 0 16px 44px rgba(69, 230, 138, 0.18);
}

.hero {
  padding: 88px 0 58px;
}

.hero-grid {
  display: grid;
  grid-template-columns: minmax(0, 0.86fr) minmax(560px, 1.14fr);
  gap: 44px;
  align-items: center;
}

.eyebrow {
  width: fit-content;
  max-width: 100%;
  gap: 8px;
  padding: 6px 10px;
  border: 1px solid rgba(80, 215, 255, 0.22);
  border-radius: 999px;
  background: rgba(80, 215, 255, 0.08);
  color: #c4f2ff;
  font-size: 0.82rem;
  font-weight: 700;
}

h1 {
  width: max-content;
  max-width: none;
  margin: 18px 0;
  font-size: clamp(3rem, 6.1vw, 6rem);
  line-height: 0.92;
  letter-spacing: -0.06em;
}

h1 span {
  display: block;
  white-space: nowrap;
}

h1 span:last-child {
  white-space: nowrap;
}

.hero-copy {
  max-width: 650px;
  margin: 0 0 28px;
  color: var(--v2-soft);
  font-size: clamp(1.08rem, 1.8vw, 1.28rem);
}

.prompt-strip {
  display: grid;
  grid-template-columns: auto 1fr;
  width: min(100%, 680px);
  overflow: hidden;
  border: 1px solid rgba(69, 230, 138, 0.28);
  border-radius: 8px;
  background: rgba(11, 14, 19, 0.82);
  box-shadow: var(--v2-shadow);
}

.prompt-label {
  display: grid;
  place-items: center;
  padding: 16px;
  background: var(--v2-green-soft);
  color: var(--v2-green);
  font-family: ui-monospace, SFMono-Regular, Consolas, monospace;
  font-weight: 800;
}

.prompt-text {
  padding: 16px 18px;
  color: var(--v2-text);
  font-weight: 750;
}

.hero-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
  margin-top: 22px;
}

.hero-showcase {
  position: relative;
  display: grid;
  gap: 14px;
}

.hero-showcase::before {
  content: "";
  position: absolute;
  inset: -34px -22px auto auto;
  z-index: -1;
  width: 78%;
  height: 70%;
  border-radius: 18px;
  background: linear-gradient(135deg, rgba(80, 215, 255, 0.2), rgba(69, 230, 138, 0.12));
  filter: blur(12px);
}

.carousel-frame {
  position: relative;
  overflow: hidden;
  border: 1px solid rgba(255, 255, 255, 0.14);
  border-radius: 8px;
  background: rgba(15, 18, 24, 0.94);
  box-shadow: var(--v2-shadow);
}

.carousel-label {
  justify-content: space-between;
  gap: 16px;
  padding: 13px 16px;
  border-bottom: 1px solid var(--v2-line);
  color: var(--v2-muted);
  font-size: 0.82rem;
  font-weight: 800;
  letter-spacing: 0.08em;
  text-transform: uppercase;
}

.carousel-header-controls,
.carousel-bottom-controls {
  display: inline-flex;
  align-items: center;
  gap: 8px;
}

.carousel-slide-count {
  display: inline-flex;
  justify-content: center;
  min-width: 6ch;
  text-align: center;
}

.carousel-header-controls button {
  display: inline-grid;
  width: 26px;
  height: 26px;
  place-items: center;
  padding: 0;
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 999px;
  background: rgba(255, 255, 255, 0.04);
  color: var(--v2-soft);
  cursor: pointer;
  transition: color 0.16s ease, border-color 0.16s ease, background 0.16s ease;
}

.carousel-header-controls button:hover {
  border-color: rgba(80, 215, 255, 0.42);
  background: rgba(80, 215, 255, 0.1);
  color: #ffffff;
}

.carousel-header-controls button i {
  font-size: 0.72rem;
}

.featured-app {
  display: grid;
  grid-template-columns: 1.28fr 0.72fr;
  height: 410px;
  color: inherit;
  text-align: left;
  cursor: pointer;
}

.featured-image {
  display: grid;
  place-items: center;
  padding: 16px 18px;
  background: #080b10;
}

.featured-image img {
  width: min(100%, 520px);
  max-height: 340px;
  object-fit: contain;
  border-radius: 6px;
  box-shadow: 0 18px 60px rgba(0, 0, 0, 0.42);
}

.featured-detail {
  display: flex;
  flex-direction: column;
  justify-content: space-between;
  min-height: 0;
  gap: 18px;
  overflow: hidden;
  padding: 24px;
  background: #172231;
}

.featured-detail > div:first-child {
  min-height: 0;
}

.featured-detail h2 {
  display: -webkit-box;
  margin: 0 0 8px;
  font-size: clamp(1.7rem, 2.5vw, 2.5rem);
  line-height: 1.05;
  letter-spacing: -0.04em;
  overflow: hidden;
  -webkit-box-orient: vertical;
  -webkit-line-clamp: 3;
}

.featured-detail p {
  margin: 0;
  color: #d8e1f0;
}

.featured-description {
  display: -webkit-box;
  font-size: 0.94rem;
  line-height: 1.45;
  overflow: hidden;
  -webkit-box-orient: vertical;
  -webkit-line-clamp: 4;
}

.creator-line {
  margin: -2px 0 10px !important;
  color: var(--v2-cyan) !important;
  font-size: 0.9rem;
  font-weight: 700;
}

.featured-thumbs {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 6px;
  flex: 0 0 auto;
}

.thumb-button {
  padding: 0;
  border: 0;
  background: transparent;
}

.featured-thumbs img {
  width: 100%;
  aspect-ratio: 16 / 9;
  object-fit: cover;
  border: 1px solid rgba(255, 255, 255, 0.16);
  border-radius: 8px;
  background: #0b0f15;
}

.carousel-controls {
  justify-content: space-between;
  gap: 14px;
  min-height: 24px;
  color: var(--v2-muted);
  font-weight: 800;
}

.carousel-dots {
  display: inline-flex;
  flex-wrap: wrap;
  gap: 6px;
}

.dot {
  width: 20px;
  height: 8px;
  padding: 0;
  border: 0;
  border-radius: 999px;
  background: rgba(255, 255, 255, 0.24);
  cursor: pointer;
}

.dot.active {
  background: var(--v2-cyan);
}

.quick-setup {
  padding: 16px;
  border: 1px solid rgba(69, 230, 138, 0.22);
  border-radius: 8px;
  background: rgba(7, 8, 11, 0.74);
  color: #d9ffe8;
  font-family: ui-monospace, SFMono-Regular, Consolas, monospace;
}

.quick-setup div + div {
  margin-top: 8px;
}

.quick-setup .muted {
  color: #758096;
}

.live-strip {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 10px;
  margin-top: 4px;
}

.live-item,
.step,
.app-card,
.template-card,
.big-card,
.creator-panel,
.stat {
  border: 1px solid var(--v2-line);
  border-radius: 8px;
  background: rgba(255, 255, 255, 0.04);
}

.live-item {
  padding: 12px;
  color: var(--v2-muted);
  font-size: 0.82rem;
}

.live-item strong {
  display: block;
  color: var(--v2-text);
  font-size: 1.12rem;
  line-height: 1.1;
}

section {
  padding: 62px 0;
}

.section-head {
  display: flex;
  align-items: end;
  justify-content: space-between;
  gap: 28px;
  margin-bottom: 24px;
}

.section-head h2 {
  max-width: 760px;
  margin: 0;
  font-size: clamp(2rem, 4vw, 3.5rem);
  line-height: 1;
  letter-spacing: -0.045em;
}

.section-head p {
  max-width: 420px;
  margin: 0;
  color: var(--v2-muted);
}

.steps,
.templates-grid,
.stat-row {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 14px;
}

.step {
  min-height: 210px;
  padding: 22px;
  background: linear-gradient(180deg, rgba(255, 255, 255, 0.055), rgba(255, 255, 255, 0.025));
}

.step-number {
  display: grid;
  width: 36px;
  height: 36px;
  place-items: center;
  margin-bottom: 26px;
  border-radius: 8px;
  background: var(--v2-cyan-soft);
  color: var(--v2-cyan);
  font-weight: 900;
}

.step h3,
.template-card h3 {
  margin: 0 0 9px;
  font-size: 1.2rem;
}

.step p,
.template-card p,
.big-card p,
.creator-top p {
  margin: 0;
  color: var(--v2-muted);
}

.badge-row,
.template-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 7px;
  margin-bottom: 10px;
}

.badge {
  padding: 4px 8px;
  border-radius: 999px;
  background: rgba(80, 215, 255, 0.12);
  color: #bdefff;
  font-size: 0.74rem;
  font-weight: 900;
  letter-spacing: 0.04em;
  text-transform: uppercase;
}

.badge.template {
  background: var(--v2-green-soft);
  color: var(--v2-green);
}

.app-info h3 {
  margin: 0 0 7px;
  font-size: 1rem;
}

.app-info p {
  min-height: 42px;
  margin: 0 0 12px;
  color: var(--v2-muted);
  font-size: 0.9rem;
}

.meta {
  justify-content: space-between;
  gap: 10px;
  color: #778196;
  font-size: 0.8rem;
  font-weight: 700;
}

.feature-band {
  display: grid;
  grid-template-columns: 0.9fr 1.1fr;
  gap: 18px;
  align-items: stretch;
}

.big-card,
.creator-panel {
  box-shadow: var(--v2-shadow);
}

.big-card {
  padding: 28px;
}

.big-card h2 {
  margin: 0 0 12px;
  font-size: clamp(2rem, 4vw, 3.8rem);
  line-height: 0.98;
  letter-spacing: -0.05em;
}

.stat-row {
  margin-top: 20px;
}

.stat {
  padding: 14px;
  background: rgba(0, 0, 0, 0.12);
}

.stat strong {
  display: block;
  font-size: 1.5rem;
  line-height: 1;
}

.stat span {
  color: var(--v2-muted);
  font-size: 0.82rem;
}

.creator-panel {
  overflow: hidden;
}

.creator-top {
  justify-content: space-between;
  gap: 16px;
  padding: 20px;
  border-bottom: 1px solid var(--v2-line);
}

.creator-top h3 {
  margin: 0 0 6px;
}

.build-feed {
  display: grid;
  gap: 1px;
  background: var(--v2-line);
}

.feed-item {
  display: grid;
  grid-template-columns: 96px 1fr auto;
  gap: 12px;
  align-items: center;
  padding: 14px 20px;
  background: rgba(16, 18, 24, 0.96);
  color: var(--v2-soft);
  font-size: 0.92rem;
}

.feed-item code {
  color: var(--v2-green);
  font-family: ui-monospace, SFMono-Regular, Consolas, monospace;
}

.status {
  padding: 4px 8px;
  border-radius: 999px;
  background: var(--v2-green-soft);
  color: var(--v2-green);
  font-size: 0.75rem;
  font-weight: 800;
}

.cta {
  padding: 72px 0 92px;
}

.cta-box {
  display: grid;
  grid-template-columns: 1fr auto;
  gap: 28px;
  align-items: center;
  padding: 30px;
  border: 1px solid rgba(69, 230, 138, 0.28);
  border-radius: 8px;
  background:
    linear-gradient(135deg, rgba(69, 230, 138, 0.1), rgba(80, 215, 255, 0.05)),
    rgba(255, 255, 255, 0.04);
  box-shadow: var(--v2-shadow);
}

.cta-copy {
  min-width: 0;
}

.cta-command-row {
  display: flex;
  align-items: center;
  gap: 14px;
  margin-top: 14px;
}

.cta-actions {
  position: relative;
  display: grid;
  justify-items: end;
  gap: 10px;
}

.copy-notice {
  position: absolute;
  top: calc(100% + 8px);
  right: 0;
  padding: 8px 10px;
  border: 1px solid rgba(69, 230, 138, 0.28);
  border-radius: 8px;
  background: rgba(7, 8, 11, 0.96);
  color: #d9ffe8;
  font-size: 0.82rem;
  font-weight: 700;
  white-space: nowrap;
  box-shadow: 0 16px 44px rgba(0, 0, 0, 0.28);
}

.cta-command-preview {
  width: min(100%, 520px);
  flex: 1 1 auto;
  padding: 12px 14px;
  border: 1px solid rgba(69, 230, 138, 0.18);
  border-radius: 8px;
  background: rgba(7, 8, 11, 0.72);
  color: #d9ffe8;
  font-family: ui-monospace, SFMono-Regular, Consolas, monospace;
  font-size: 0.88rem;
}

.cta-command-preview div + div {
  margin-top: 6px;
}

.cta h2 {
  margin: 0 0 8px;
  font-size: clamp(1.8rem, 3vw, 3rem);
  letter-spacing: -0.04em;
}

.cta p {
  margin: 0;
  color: var(--v2-muted);
}

footer {
  padding: 24px 0 34px;
  border-top: 1px solid var(--v2-line);
  color: var(--v2-muted);
  font-size: 0.9rem;
}

.footer-inner {
  justify-content: space-between;
  gap: 20px;
}

@media (max-width: 980px) {
  .hero-grid,
  .featured-app,
  .feature-band,
  .cta-box {
    grid-template-columns: 1fr;
  }

  .featured-app {
    height: auto;
    max-height: 720px;
    overflow: hidden;
  }

  .featured-image {
    min-height: 0;
    max-height: 300px;
  }

  .featured-image img {
    max-height: 260px;
  }

  h1 {
    width: auto;
    max-width: 690px;
  }

  .steps,
  .nav-links {
    display: none;
  }

}

@media (max-width: 640px) {
  .shell {
    width: min(100% - 28px, 1180px);
  }

  .hero {
    padding-top: 54px;
  }

  .nav-actions {
    display: none;
  }

  h1 span {
    white-space: normal;
  }

  .big-card,
  .cta-box {
    padding: 18px;
  }

  .cta-actions {
    justify-items: stretch;
  }

  .copy-notice {
    position: static;
    white-space: normal;
  }

  .cta-command-row {
    display: grid;
  }

  .cta-command-preview {
    width: 100%;
  }

  .prompt-strip,
  .feed-item {
    grid-template-columns: 1fr;
  }

  .prompt-label {
    place-items: start;
  }

  .carousel-label {
    align-items: flex-start;
  }

  .carousel-header-controls {
    flex-shrink: 0;
  }

  .featured-app {
    max-height: 650px;
  }

  .featured-image {
    max-height: 240px;
    padding: 14px;
  }

  .featured-image img {
    max-height: 210px;
  }

  .featured-detail {
    gap: 12px;
    padding: 18px;
  }

  .steps,
  .templates-grid,
  .live-strip,
  .stat-row {
    grid-template-columns: 1fr;
  }

  .section-head,
  .footer-inner {
    display: grid;
  }
}
</style>
