<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import PublicSiteHeader from '@/components/PublicSiteHeader.vue'
import { publicAppConfig } from '@/config/publicAppConfig'
import { resolveMarketplaceAssetUrl, useMarketplaceApps } from '@/composables/useMarketplaceApps'
import type { MarketplaceAppCard } from '@/types/marketplace'
import logoUrl from '@/assets/logo.svg'
import studioScreenshotUrl from '@/assets/studio-apps-screenshot.jpg'

const router = useRouter()
const { featuredApps, loadApps } = useMarketplaceApps(12)
const activeIndex = ref(0)
const isCarouselPaused = ref(false)
const promptCopied = ref(false)
let rotationId: number | undefined
let promptResetId: number | undefined

const aiStarterPrompt = `Build [APP IDEA] as an Automaton Monster app.

Read https://automaton.monster/llms.txt before making changes.

Use the Automaton CLI scaffold and the canonical full-stack example if needed.

When deployment requires authentication, ask me to run automaton login in the terminal.
Never ask me to paste API keys into AI chat.`

const heroApps = computed<MarketplaceAppCard[]>(() => {
  const featured = featuredApps.value.slice(0, 3)
  return featured
})

const activeApp = computed(() => heroApps.value[activeIndex.value] ?? heroApps.value[0])
const activeAppCopy = computed(() => {
  const app = activeApp.value
  if (!app) return ''

  return app.summary || app.description || 'Live apps become templates, remixes, and proof that one person can ship useful software with an AI agent.'
})
const sideApps = computed(() =>
  heroApps.value
    .map((app, index) => ({ app, index }))
    .filter((item) => item.index !== activeIndex.value)
    .slice(0, 2),
)
const activeMetaImageUrl = computed(() => activeApp.value?.thumbnailUrl || '')
const activeCreatorName = computed(() => activeApp.value?.creatorDisplayName || 'Automaton creator')
const previewCardImage = (app: MarketplaceAppCard) => {
  if (app.thumbnailUrl) {
    return app.thumbnailUrl
  }

  const firstScreenshot = app.screenshots?.[0]?.imageUrl
  return firstScreenshot || ''
}
const previewCardCopy = (app: MarketplaceAppCard) => {
  return app.summary || app.description || 'Published Automaton app'
}

function appImageUrl(url?: string | null) {
  if (!url) return ''
  if (url.startsWith('data:') || url.startsWith('blob:')) return url
  if (url.startsWith('http')) return url
  if (url.startsWith('/src/') || url.startsWith('/assets/')) return url
  return resolveMarketplaceAssetUrl(url)
}

async function copyAiPrompt() {
  try {
    await navigator.clipboard.writeText(aiStarterPrompt)
    promptCopied.value = true
    if (promptResetId !== undefined) {
      window.clearTimeout(promptResetId)
    }
    promptResetId = window.setTimeout(() => {
      promptCopied.value = false
      promptResetId = undefined
    }, 2400)
  } catch {
    promptCopied.value = false
  }
}

function previousApp() {
  if (heroApps.value.length <= 1) return
  activeIndex.value = (activeIndex.value - 1 + heroApps.value.length) % heroApps.value.length
}

function nextApp() {
  if (heroApps.value.length <= 1) return
  activeIndex.value = (activeIndex.value + 1) % heroApps.value.length
}

function selectApp(index: number) {
  activeIndex.value = index
}

function pauseCarousel() {
  isCarouselPaused.value = true
}

function resumeCarousel() {
  isCarouselPaused.value = false
}

function handleCarouselLeave() {
  resumeCarousel()
}

function openActiveApp() {
  const app = activeApp.value
  openAppDetails(app)
}

function openAppDetails(app?: MarketplaceAppCard) {
  if (!app) return
  void router.push(`/apps/${app.id}`)
}

function startRotation() {
  stopRotation()
  rotationId = window.setInterval(() => {
    if (!isCarouselPaused.value && heroApps.value.length > 1) {
      nextApp()
    }
  }, 6500)
}

function stopRotation() {
  if (rotationId !== undefined) {
    window.clearInterval(rotationId)
    rotationId = undefined
  }
}

onMounted(() => {
  void loadApps()
  startRotation()
})

onBeforeUnmount(() => {
  stopRotation()
  if (promptResetId !== undefined) {
    window.clearTimeout(promptResetId)
  }
})

watch(
  () => heroApps.value.length,
  (length) => {
    if (activeIndex.value >= length) {
      activeIndex.value = 0
    }
  },
)
</script>

<template>
  <div class="home-v2">
    <PublicSiteHeader />

    <main>
      <header class="hero">
        <div class="shell hero-grid">
          <div>
            <span class="eyebrow">Use any AI. Keep your backend.</span>
            <h1>
              <span>Build with AI.</span>
              <span>Put it online.</span>
            </h1>
            <p class="hero-copy">
              Use Claude Code, Codex, Cursor, Copilot, or Gemini. Frontend hosting included. Backend runs where you choose.
            </p>
            <div class="hero-actions">
              <a class="button primary" href="/docs/getting-started">Get Started</a>
              <button class="button" type="button" @click="copyAiPrompt">
                {{ promptCopied ? 'Prompt Copied' : 'Copy AI Prompt' }}
              </button>
            </div>
          </div>

          <div class="hero-showcase">
            <div
              class="carousel-frame"
              @mouseenter="pauseCarousel"
              @mouseleave="handleCarouselLeave"
              @focusin="pauseCarousel"
              @focusout="resumeCarousel"
            >
              <div class="carousel-label">
                <span>Built with Automaton</span>
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

              <section v-if="activeApp" class="featured-shelf" aria-label="Featured apps">
                <article class="featured-stage">
                  <button
                    class="featured-image-button"
                    type="button"
                    @click="openActiveApp"
                    @keydown.enter.prevent="openActiveApp"
                  >
                    <img
                      :src="appImageUrl(activeMetaImageUrl)"
                      :alt="`${activeApp.name} cover`"
                    />
                  </button>

                  <div class="featured-meta">
                    <div class="featured-copy">
                      <h2>{{ activeApp.name }}</h2>
                      <p class="creator-line">by {{ activeCreatorName }}</p>
                      <p class="featured-description">{{ activeAppCopy }}</p>
                    </div>
                  </div>
                </article>

                <aside class="featured-previews" aria-label="More featured apps">
                  <button
                    v-for="item in sideApps"
                    :key="item.app.id"
                    class="preview-card"
                    type="button"
                    :aria-label="`Open ${item.app.name}`"
                    @click="openAppDetails(item.app)"
                  >
                    <div class="preview-media">
                      <img :src="appImageUrl(previewCardImage(item.app))" :alt="item.app.name" />
                    </div>
                    <div class="preview-copy">
                      <h3>{{ item.app.name }}</h3>
                      <p>{{ previewCardCopy(item.app) }}</p>
                    </div>
                  </button>
                </aside>
              </section>

              <div v-if="heroApps.length > 1" class="carousel-controls">
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
          </div>
        </div>
      </header>

      <section id="architecture" class="architecture-section">
        <div class="shell">
          <div class="architecture-intro">
            <div>
              <span class="eyebrow">Deployment architecture</span>
              <h2>One Platform. Two Deployment Paths.</h2>
            </div>

            <div>
              <div class="path-cards">
                <div class="path-card frontend">
                  <span>Frontend</span>
                  <strong>Hosted by Automaton</strong>
                </div>
                <div class="path-card backend">
                  <span>Backend</span>
                  <strong>Runs where you choose</strong>
                </div>
              </div>
            </div>
          </div>

          <div class="diagram-placeholder">
            <div class="architecture-card">
              <div class="architecture-diagram" aria-label="Automaton architecture diagram">
                <div class="architecture-box architecture-box-ai">
                  <h4>AI Coding Tools</h4>
                  <div class="architecture-item-list">
                    <div class="architecture-item"><span class="architecture-icon architecture-icon-orange">*</span> Claude Code</div>
                    <div class="architecture-item"><span class="architecture-icon">◎</span> OpenAI Codex</div>
                    <div class="architecture-item"><span class="architecture-icon architecture-icon-cyan">◇</span> Cursor</div>
                    <div class="architecture-item"><span class="architecture-icon architecture-icon-purple">⌘</span> GitHub Copilot</div>
                    <div class="architecture-item"><span class="architecture-icon architecture-icon-cyan">✦</span> Gemini</div>
                    <div class="architecture-item"><span class="architecture-icon architecture-icon-purple">M</span> Any MCP Client</div>
                  </div>
                </div>

                <div class="architecture-arrow-wrap"><div class="architecture-arrow"></div></div>

                <div>
                  <div class="architecture-box architecture-box-cli">
                    <div class="terminal-icon">&gt;_</div>
                    <h4>Automaton CLI</h4>
                    <div class="cli-inner">
                      <div class="checkline">Initialize project</div>
                      <div class="checkline">Configure runtime</div>
                      <div class="checkline">Deploy and manage</div>
                    </div>
                  </div>

                  <div class="down-arrow"></div>

                  <div class="architecture-box architecture-box-frontend">
                    <h4>Your Frontend</h4>
                    <p class="architecture-subcopy">Hosted by Automaton</p>
                    <div class="frontend-icons">
                      <div class="mini"><span class="architecture-icon">⚡</span>Global CDN</div>
                      <div class="mini"><span class="architecture-icon">🔒</span>HTTPS</div>
                      <div class="mini"><span class="architecture-icon">↯</span>Edge caching</div>
                    </div>
                  </div>
                </div>

                <div class="architecture-arrow-wrap"><div class="architecture-arrow architecture-arrow-purple"></div></div>

                <div class="architecture-box architecture-box-backend">
                  <h4>Your Backend</h4>
                  <p class="architecture-subcopy">Backend runs where you choose</p>
                  <div class="architecture-item-list architecture-backend-list">
                    <div class="architecture-item"><span class="architecture-icon architecture-icon-purple">▭</span> Laptop</div>
                    <div class="architecture-item"><span class="architecture-icon architecture-icon-purple">▤</span> Home Server / NAS</div>
                    <div class="architecture-item"><span class="architecture-icon architecture-icon-purple">☁</span> VPS / Cloud</div>
                    <div class="architecture-item"><span class="architecture-icon architecture-icon-purple">▥</span> Company Network</div>
                    <div class="architecture-item"><span class="architecture-icon architecture-icon-purple">●</span> Raspberry Pi</div>
                    <div class="architecture-item"><span class="architecture-icon architecture-icon-purple">◎</span> Anywhere</div>
                  </div>
                </div>
              </div>

              <div class="mobile-diagram" aria-label="Simplified Automaton architecture diagram">
                <div class="mobile-step">
                  <h4>1. Use any AI</h4>
                  <p>Claude Code, Codex, Cursor, Copilot, Gemini, or any MCP client.</p>
                </div>
                <div class="mobile-arrow">↓</div>
                <div class="mobile-step">
                  <h4>2. Deploy with Automaton CLI</h4>
                  <p>Initialize the project, configure runtime, and deploy with a repeatable workflow.</p>
                </div>
                <div class="mobile-arrow">↙︎ ↘︎</div>
                <div class="mobile-step">
                  <h4>3. Frontend hosted by Automaton</h4>
                  <p>Public, fast, HTTPS-enabled static app hosting.</p>
                </div>
                <div class="mobile-step">
                  <h4>4. Backend runs on your runner</h4>
                  <p>Laptop, VPS, NAS, company network, Raspberry Pi, or anywhere else you want it.</p>
                </div>
              </div>
            </div>
          </div>
        </div>
      </section>

      <section id="ai-workflow" class="ai-workflow-section">
        <div class="shell">
          <div class="ai-workflow-grid">
            <div class="ai-workflow-copy">
              <span class="eyebrow">AI workflow</span>
              <h2>Describe the app. Let AI build it.</h2>
              <p class="ai-workflow-lead">
                Paste one starter prompt into Cursor, Claude Code, Codex, Copilot Chat, or Gemini. Your AI agent reads
                Automaton&apos;s app instructions, creates the project with the CLI, and prepares it for deployment.
              </p>
              <p class="ai-workflow-note">
                You only enter your Automaton API key in the terminal when `automaton login` asks.
              </p>
              <p class="ai-workflow-safety">
                Never paste your API key into AI chat.
              </p>

              <div class="ai-workflow-actions">
                <button class="button primary" type="button" @click="copyAiPrompt">
                  {{ promptCopied ? 'Prompt Copied' : 'Copy Starter Prompt' }}
                </button>
                <a class="ai-workflow-link" href="/docs/build-an-automaton-app">Read app instructions</a>
              </div>
            </div>

            <div class="ai-steps-card">
              <span class="eyebrow">How it works</span>
              <ol class="ai-steps-list">
                <li>Describe the app you want.</li>
                <li>Paste the starter prompt into your AI coding tool.</li>
                <li>The agent scaffolds the app with Automaton CLI.</li>
                <li>You approve the code and deploy.</li>
              </ol>

              <div class="prompt-preview-card">
                <strong>Prompt preview</strong>
                <p>Build [APP IDEA] as an Automaton Monster app.</p>
                <p>Read `https://automaton.monster/llms.txt` before making changes.</p>
                <p>Use the Automaton CLI scaffold and the canonical full-stack example if needed.</p>
                <p>When deployment requires authentication, ask me to run `automaton login` in the terminal.</p>
                <p>Never ask me to paste API keys into AI chat.</p>
              </div>
            </div>
          </div>
        </div>
      </section>

      <section id="build">
        <div class="shell">
          <div class="install-workflow">
            <div class="install-copy">
              <span class="eyebrow">Deployment</span>
              <h2>Deploy with one command.</h2>
              <p class="install-lead">
                Your AI writes the code. Automaton gets it online. Build apps with any coding agent, then publish them without learning cloud infrastructure.
              </p>

              <div class="install-points">
                <div class="install-point">Use any AI coding tool</div>
                <div class="install-point">Frontend hosting included</div>
                <div class="install-point">Backend stays under your control</div>
              </div>
            </div>

            <div class="terminal-shell">
              <div class="terminal-top">
                <span class="terminal-dot"></span>
                <span class="terminal-dot"></span>
                <span class="terminal-dot"></span>
                <span class="terminal-title">automaton / deploy</span>
              </div>

              <div id="install" class="terminal-body">
                <span class="terminal-comment"># install once</span><br />
                <span class="terminal-prompt">$</span> <span class="terminal-command">dotnet tool install -g Automaton.Cli</span><br />
                <span class="terminal-prompt">$</span> <span class="terminal-command">automaton login</span><br />
                <br />
                <span class="terminal-comment"># build with AI</span><br />
                <br />
                <span class="terminal-prompt">$</span> <span class="terminal-command">automaton deploy</span><br />
                <span class="terminal-success">✓ frontend online</span><br />
                <span class="terminal-success">✓ backend connected</span>
              </div>

              <div class="terminal-result-grid">
                <div class="terminal-result-card frontend">
                  <strong>Frontend</strong>
                  <h3>Hosted by Automaton</h3>
                </div>

                <div class="terminal-result-card backend">
                  <strong>Backend</strong>
                  <h3>Runs where you choose</h3>
                </div>
              </div>
            </div>
          </div>
        </div>
      </section>

      <section id="studio">
        <div class="shell">
          <div class="studio-section">
            <div>
              <span class="eyebrow">Automaton Studio</span>
              <h2>Manage everything around your apps.</h2>
              <p class="studio-lead">
                The CLI builds and deploys. Automaton Studio gives you the dashboard for apps, runners, publishing, API keys, screenshots, metadata, and runtime status.
              </p>

              <div class="studio-feature-list">
                <div class="studio-feature">Manage apps and deployments</div>
                <div class="studio-feature">Add covers and screenshots</div>
                <div class="studio-feature">Create and rotate API keys</div>
                <div class="studio-feature">Monitor runner status</div>
                <div class="studio-feature">Edit summaries and descriptions</div>
                <div class="studio-feature">Publish, fork, and organize apps</div>
              </div>

              <div class="studio-actions">
                <a :href="publicAppConfig.studioUrl" target="_blank" rel="noopener" class="button primary">Open Studio</a>
                <a href="/docs/" class="button">Read docs</a>
              </div>
            </div>

            <div class="studio-card">
              <div class="browser-top">
                <span class="terminal-dot"></span>
                <span class="terminal-dot"></span>
                <span class="terminal-dot"></span>
                <span class="browser-title">studio.automaton.monster</span>
              </div>

              <div class="studio-screenshot-slot">
                <img
                  :src="studioScreenshotUrl"
                  alt="Automaton Studio apps dashboard showing app creation, app list, and publishing controls"
                />
              </div>

              <div class="studio-caption">
                <div class="caption-card">
                  <strong>Apps</strong>
                  <span>Manage metadata, screenshots, covers, publishing, and forks.</span>
                </div>
                <div class="caption-card">
                  <strong>Runners</strong>
                  <span>See backend runner status and connected runtime environments.</span>
                </div>
                <div class="caption-card">
                  <strong>Keys</strong>
                  <span>Create project API keys for CLI, SDKs, and integrations.</span>
                </div>
              </div>
            </div>
          </div>
        </div>
      </section>

      <section id="runners">
        <div class="shell runners-band">
          <div class="runners-copy">
            <h2>Your backend. Your infrastructure.</h2>
            <p>
              Frontend hosting included. Backend runs where you choose. Run Python and backend services on infrastructure you already trust instead of moving everything into a closed platform.
            </p>
            <div class="runner-chip-grid">
              <span class="runner-chip">Laptop</span>
              <span class="runner-chip">VPS</span>
              <span class="runner-chip">NAS</span>
              <span class="runner-chip">Company network</span>
              <span class="runner-chip">Raspberry Pi</span>
            </div>
          </div>

          <div class="runners-panel">
            <div class="runners-panel-header">
              <h3>Backend runtime options</h3>
              <span class="status">Your infra</span>
            </div>
            <div class="runners-list">
              <div class="runner-item">
                <strong>Laptop</strong>
                <p>Local development, fast iteration, private tools.</p>
              </div>
              <div class="runner-item">
                <strong>VPS / Cloud</strong>
                <p>Public APIs, scheduled jobs, scalable workloads.</p>
              </div>
              <div class="runner-item">
                <strong>Home Server / NAS</strong>
                <p>Internal dashboards, family systems, media and file automation.</p>
              </div>
              <div class="runner-item">
                <strong>Company Network</strong>
                <p>Keep access close to internal databases and business systems.</p>
              </div>
              <div class="runner-item">
                <strong>Raspberry Pi</strong>
                <p>Edge devices, sensors, lightweight always-on automation.</p>
              </div>
            </div>
          </div>
        </div>
      </section>

      <section class="cta">
        <div class="shell cta-box">
          <div class="cta-copy">
            <h2>Ready to build?</h2>
            <p>Install Automaton CLI and connect it to your favorite AI coding agent.</p>
          </div>
          <div class="cta-actions">
            <RouterLink class="button primary" to="/cli">Get started</RouterLink>
          </div>
        </div>
      </section>
    </main>

    <footer class="footer">
      <div class="shell footer-shell">
        <div class="footer-positioning">
          <div>
            <strong>Frontend hosted by Automaton.</strong>
            <span>Backend executes on infrastructure you control.</span>
          </div>

          <div class="status-pills">
            <span class="pill green">Beta</span>
            <span class="pill">BYOK</span>
            <span class="pill">Self-hosted runners</span>
            <span class="pill">Frontend hosting included</span>
          </div>
        </div>

        <div class="footer-grid">
          <div class="brand-column">
            <div class="brand-row">
              <img :src="logoUrl" alt="" class="footer-logo" aria-hidden="true" />
              <span class="brand-name">Automaton Monster</span>
            </div>
            <p class="brand-copy">
              Build with AI. Put it online. Automaton connects AI-generated apps to frontend hosting and runners you control.
            </p>
          </div>

          <div class="footer-column">
            <h3>Product</h3>
            <div class="link-list">
              <a href="/apps">Apps</a>
              <a href="/#architecture">How it works</a>
              <a href="/pricing">Pricing</a>
              <a :href="publicAppConfig.studioUrl" target="_blank" rel="noopener">Studio</a>
            </div>
          </div>

          <div class="footer-column">
            <h3>Resources</h3>
            <div class="link-list">
              <a href="/docs/">Docs</a>
              <a href="/cli">CLI setup</a>
              <a href="/#runners">Runners</a>
              <a href="/apps">Publishing</a>
            </div>
          </div>

          <div class="footer-column">
            <h3>Legal</h3>
            <div class="link-list">
              <a href="#">Privacy</a>
              <a href="#">Terms</a>
              <a href="mailto:hello@automaton.monster">Contact</a>
            </div>
          </div>
        </div>

        <div class="footer-bottom">
          <span>© 2026 Automaton Monster</span>
          <div class="bottom-links">
            <a href="/apps">Public apps</a>
            <a :href="publicAppConfig.studioUrl" target="_blank" rel="noopener">Open Studio</a>
          </div>
        </div>
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
  width: min(1360px, calc(100% - 40px));
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
  cursor: pointer;
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
  grid-template-columns: minmax(0, 0.84fr) minmax(0, 1.16fr);
  gap: 44px;
  align-items: start;
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
  width: auto;
  max-width: 520px;
  margin: 18px 0;
  font-size: clamp(3rem, 5.2vw, 5.5rem);
  line-height: 0.92;
  letter-spacing: -0.06em;
}

h1 span {
  display: block;
  white-space: normal;
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
  min-width: 0;
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
  min-height: 430px;
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

.featured-shelf {
  display: grid;
  grid-template-columns: minmax(0, 1.8fr) minmax(240px, 0.62fr);
  min-height: 392px;
  height: 392px;
  padding: 0;
}

.featured-stage {
  display: grid;
  grid-template-rows: minmax(0, 1fr) auto;
  min-height: 392px;
  border-right: 1px solid rgba(255, 255, 255, 0.08);
  align-self: stretch;
}

.featured-image-button {
  display: grid;
  place-items: start center;
  height: 100%;
  min-height: 0;
  padding: 0 12px 0;
  border: 0;
  background: #080b10;
  cursor: pointer;
}

.featured-image-button img {
  width: 100%;
  height: 100%;
  min-height: 0;
  object-fit: cover;
  object-position: center;
  border-radius: 10px;
  box-shadow: 0 18px 60px rgba(0, 0, 0, 0.42);
}

.featured-meta {
  display: grid;
  gap: 8px;
  padding: 12px 20px 14px;
  background:
    linear-gradient(180deg, rgba(10, 16, 23, 0), rgba(10, 16, 23, 0.82) 22%, rgba(10, 16, 23, 0.98) 100%);
}

.featured-copy {
  min-width: 0;
}

.featured-meta h2 {
  margin: 0;
  font-size: clamp(1.3rem, 1.8vw, 1.9rem);
  line-height: 1.02;
  letter-spacing: -0.04em;
}

.featured-description {
  margin: 8px 0 0;
  color: #d8e1f0;
  font-size: 0.94rem;
  line-height: 1.45;
  display: -webkit-box;
  overflow: hidden;
  -webkit-box-orient: vertical;
  -webkit-line-clamp: 3;
}

.creator-line {
  margin: 6px 0 0 !important;
  color: var(--v2-cyan) !important;
  font-size: 0.9rem;
  font-weight: 700;
}

.featured-previews {
  display: grid;
  grid-template-rows: repeat(2, minmax(0, 1fr));
  gap: 1px;
  min-height: 392px;
  height: 392px;
  background: rgba(255, 255, 255, 0.08);
}

.preview-card {
  display: grid;
  grid-template-rows: minmax(0, 1fr) auto;
  padding: 0;
  border: 0;
  background: #172231;
  color: inherit;
  text-align: left;
  cursor: pointer;
  transition: background 0.18s ease;
}

.preview-card:hover {
  background: #1c2a3b;
}

.preview-media {
  min-height: 0;
  padding: 10px 10px 0;
}

.preview-media img {
  width: 100%;
  height: 100%;
  min-height: 0;
  object-fit: cover;
  border-radius: 10px;
  background: #0b0f15;
}

.preview-copy {
  padding: 12px 14px 14px;
  min-height: 88px;
}

.preview-copy h3 {
  margin: 0 0 6px;
  font-size: 0.96rem;
  line-height: 1.1;
}

.preview-copy p {
  margin: 0;
  color: #b9c4d5;
  font-size: 0.8rem;
  line-height: 1.35;
  display: -webkit-box;
  overflow: hidden;
  -webkit-box-orient: vertical;
  -webkit-line-clamp: 2;
}

.carousel-controls {
  padding: 0 16px 6px;
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

.architecture-section {
  padding-top: 12px;
}

.ai-workflow-section {
  padding: 12px 0 0;
}

.ai-workflow-grid {
  display: grid;
  grid-template-columns: minmax(0, 1.05fr) minmax(320px, 0.95fr);
  gap: 22px;
}

.ai-workflow-copy,
.ai-steps-card {
  padding: 24px;
  border: 1px solid var(--v2-line);
  border-radius: 20px;
  background: rgba(255, 255, 255, 0.025);
}

.ai-workflow-copy h2 {
  margin: 0;
  font-size: clamp(2rem, 4vw, 3.1rem);
  line-height: 0.98;
  letter-spacing: -0.05em;
}

.ai-workflow-lead {
  margin: 18px 0 0;
  color: var(--v2-muted);
  line-height: 1.7;
}

.ai-workflow-note {
  margin: 14px 0 0;
  color: var(--v2-text);
  font-weight: 700;
  line-height: 1.6;
}

.ai-workflow-safety {
  margin: 10px 0 0;
  color: var(--v2-muted);
  font-size: 0.94rem;
  line-height: 1.55;
}

.ai-workflow-actions {
  display: flex;
  gap: 12px;
  margin-top: 18px;
  flex-wrap: wrap;
  align-items: center;
}

.ai-workflow-link {
  color: var(--v2-cyan);
  font-weight: 700;
}

.ai-workflow-link:hover {
  color: var(--v2-text);
}

.ai-steps-list {
  display: grid;
  gap: 10px;
  margin: 18px 0 0;
  padding-left: 20px;
  color: var(--v2-muted);
  line-height: 1.65;
}

.prompt-preview-card {
  margin-top: 24px;
  padding: 18px;
  border: 1px solid rgba(69, 230, 138, 0.22);
  border-radius: 16px;
  background: rgba(7, 8, 11, 0.72);
}

.prompt-preview-card strong {
  display: block;
  margin-bottom: 10px;
  color: var(--v2-text);
}

.prompt-preview-card p {
  margin: 0;
  color: var(--v2-muted);
  font-family: ui-monospace, SFMono-Regular, Consolas, monospace;
  font-size: 0.92rem;
  line-height: 1.65;
}

.architecture-intro {
  display: grid;
  grid-template-columns: minmax(0, 0.95fr) minmax(320px, 0.65fr);
  gap: 32px;
  align-items: end;
  margin-bottom: 28px;
}

.architecture-intro h2 {
  max-width: 820px;
  margin: 0;
  font-size: clamp(2.25rem, 5vw, 4.5rem);
  line-height: 0.96;
  letter-spacing: -0.06em;
}

.architecture-summary {
  display: grid;
  gap: 12px;
  margin: 0;
  color: var(--v2-soft);
  font-size: clamp(1rem, 1.5vw, 1.18rem);
  line-height: 1.55;
}

.architecture-summary strong {
  color: var(--v2-text);
  font-weight: 800;
}

.path-cards {
  display: grid;
  grid-template-columns: repeat(2, 1fr);
  gap: 14px;
  margin-top: 26px;
}

.path-card {
  padding: 18px;
  border: 1px solid var(--v2-line);
  border-radius: 18px;
  background: rgba(255, 255, 255, 0.035);
}

.path-card.frontend {
  border-color: rgba(80, 215, 255, 0.24);
}

.path-card.backend {
  border-color: rgba(69, 230, 138, 0.3);
}

.path-card span {
  display: block;
  margin-bottom: 8px;
  color: var(--v2-muted);
  font-size: 0.78rem;
  font-weight: 900;
  letter-spacing: 0.11em;
  text-transform: uppercase;
}

.path-card strong {
  display: block;
  color: var(--v2-text);
  font-size: 1.1rem;
  letter-spacing: -0.025em;
}

.diagram-placeholder {
  display: grid;
  min-height: 360px;
  place-items: stretch;
  padding: 0;
  border: 1px dashed rgba(69, 230, 138, 0.32);
  border-radius: 28px;
  background:
    radial-gradient(circle at 50% 0%, rgba(69, 230, 138, 0.12), transparent 30%),
    rgba(255, 255, 255, 0.025);
}

.architecture-card {
  position: relative;
  overflow: hidden;
  border: 0;
  border-radius: 27px;
  padding: 34px;
  background:
    radial-gradient(circle at 50% 0%, rgba(69, 230, 138, 0.14), transparent 28%),
    linear-gradient(180deg, rgba(255, 255, 255, 0.055), rgba(255, 255, 255, 0.02));
  box-shadow: var(--v2-shadow);
}

.architecture-diagram {
  display: grid;
  grid-template-columns: 1fr 170px 1.15fr 170px 1fr;
  align-items: center;
  min-height: 540px;
}

.architecture-box {
  position: relative;
  border: 1px solid var(--v2-line);
  border-radius: 22px;
  padding: 22px;
  background: rgba(8, 13, 19, 0.82);
  box-shadow: inset 0 1px 0 rgba(255, 255, 255, 0.05);
}

.architecture-box-ai {
  border-color: rgba(80, 215, 255, 0.26);
}

.architecture-box-cli {
  border-color: rgba(69, 230, 138, 0.72);
  box-shadow: 0 0 45px rgba(69, 230, 138, 0.1), inset 0 1px 0 rgba(255, 255, 255, 0.05);
}

.architecture-box-backend {
  border-color: rgba(155, 108, 255, 0.48);
}

.architecture-box-frontend {
  text-align: center;
  border-color: rgba(69, 230, 138, 0.42);
  background: rgba(10, 28, 20, 0.72);
}

.architecture-box h4,
.mobile-step h4 {
  margin: 0 0 16px;
  font-size: 1.35rem;
  letter-spacing: -0.025em;
}

.architecture-box-ai h4 {
  color: #dff8ff;
}

.architecture-box-cli h4,
.architecture-box-frontend h4 {
  color: var(--v2-green);
}

.architecture-box-backend h4 {
  color: #b891ff;
}

.architecture-item-list {
  display: grid;
  gap: 10px;
}

.architecture-item {
  display: flex;
  align-items: center;
  gap: 12px;
  min-height: 48px;
  padding: 12px;
  border: 1px solid rgba(255, 255, 255, 0.09);
  border-radius: 14px;
  background: linear-gradient(180deg, rgba(255, 255, 255, 0.045), rgba(255, 255, 255, 0.018));
  color: #edf4fb;
  font-weight: 700;
}

.architecture-icon {
  display: grid;
  width: 28px;
  height: 28px;
  flex: 0 0 28px;
  place-items: center;
  border-radius: 9px;
  background: rgba(69, 230, 138, 0.12);
  color: var(--v2-green);
  font-weight: 900;
}

.architecture-icon-purple {
  background: rgba(155, 108, 255, 0.14);
  color: #9b6cff;
}

.architecture-icon-cyan {
  background: rgba(80, 215, 255, 0.12);
  color: var(--v2-cyan);
}

.architecture-icon-orange {
  background: rgba(255, 139, 93, 0.12);
  color: #ff8b5d;
}

.architecture-arrow-wrap {
  display: grid;
  height: 100%;
  place-items: center;
}

.architecture-arrow {
  position: relative;
  width: 128px;
  height: 54px;
}

.architecture-arrow::before {
  content: "";
  position: absolute;
  top: 50%;
  right: 20px;
  left: 0;
  border-top: 4px dashed var(--v2-green);
  filter: drop-shadow(0 0 10px rgba(69, 230, 138, 0.55));
}

.architecture-arrow::after {
  content: "";
  position: absolute;
  top: calc(50% - 12px);
  right: 0;
  width: 24px;
  height: 24px;
  border-top: 4px solid var(--v2-green);
  border-right: 4px solid var(--v2-green);
  transform: rotate(45deg);
  filter: drop-shadow(0 0 10px rgba(69, 230, 138, 0.55));
}

.architecture-arrow-purple::before {
  border-color: #9b6cff;
  filter: drop-shadow(0 0 10px rgba(155, 108, 255, 0.55));
}

.architecture-arrow-purple::after {
  border-color: #9b6cff;
  filter: drop-shadow(0 0 10px rgba(155, 108, 255, 0.55));
}

.terminal-icon {
  display: grid;
  width: 58px;
  height: 58px;
  margin-bottom: 8px;
  place-items: center;
  border: 1px solid rgba(69, 230, 138, 0.45);
  border-radius: 14px;
  background: rgba(69, 230, 138, 0.08);
  color: var(--v2-green);
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
  font-size: 25px;
  font-weight: 900;
}

.cli-inner {
  display: grid;
  gap: 14px;
  padding: 8px 0;
}

.checkline {
  display: flex;
  align-items: center;
  gap: 12px;
  color: #dbe7f1;
  font-size: 1.04rem;
}

.checkline::before {
  content: "✓";
  display: grid;
  width: 22px;
  height: 22px;
  flex: 0 0 22px;
  place-items: center;
  border: 2px solid var(--v2-green);
  border-radius: 999px;
  color: var(--v2-green);
  font-size: 13px;
  font-weight: 900;
}

.down-arrow {
  position: relative;
  width: 4px;
  height: 76px;
  margin: 12px auto;
  border-left: 4px dashed var(--v2-green);
  filter: drop-shadow(0 0 10px rgba(69, 230, 138, 0.55));
}

.down-arrow::after {
  content: "";
  position: absolute;
  bottom: -3px;
  left: -12px;
  width: 22px;
  height: 22px;
  border-right: 4px solid var(--v2-green);
  border-bottom: 4px solid var(--v2-green);
  transform: rotate(45deg);
}

.architecture-subcopy {
  margin: -6px 0 0;
  color: var(--v2-muted);
}

.frontend-icons {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 14px;
  margin-top: 18px;
}

.mini {
  display: grid;
  gap: 8px;
  justify-items: center;
  color: #d8e2ec;
  font-size: 0.88rem;
}

.mini .architecture-icon {
  width: 46px;
  height: 46px;
  border-radius: 999px;
  font-size: 1.35rem;
}

.architecture-backend-list {
  margin-top: 18px;
}

.mobile-diagram {
  display: none;
}

.mobile-step {
  border: 1px solid rgba(255, 255, 255, 0.12);
  border-radius: 20px;
  padding: 20px;
  background: rgba(8, 13, 19, 0.82);
}

.mobile-step p {
  margin: 0;
  color: var(--v2-muted);
  line-height: 1.55;
}

.mobile-arrow {
  display: grid;
  min-height: 54px;
  place-items: center;
  color: var(--v2-green);
  font-size: 46px;
  line-height: 1;
  text-shadow: 0 0 18px rgba(69, 230, 138, 0.55);
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

.section-head-build {
  align-items: stretch;
}

.install-workflow {
  display: grid;
  grid-template-columns: 0.85fr 1.15fr;
  gap: 34px;
  align-items: center;
}

.install-copy h2 {
  max-width: 620px;
  margin: 0;
  font-size: clamp(2.6rem, 5.3vw, 4.5rem);
  line-height: 0.96;
  letter-spacing: -0.06em;
}

.install-lead {
  max-width: 560px;
  margin: 22px 0 0;
  color: var(--v2-muted);
  font-size: 1.18rem;
  line-height: 1.55;
}

.install-points {
  display: grid;
  gap: 12px;
  margin-top: 28px;
}

.install-point {
  display: flex;
  gap: 12px;
  align-items: flex-start;
  color: #dbe7f1;
  font-weight: 700;
}

.install-point::before {
  content: "✓";
  display: grid;
  width: 22px;
  height: 22px;
  flex: 0 0 22px;
  place-items: center;
  margin-top: 1px;
  border: 2px solid var(--v2-green);
  border-radius: 999px;
  color: var(--v2-green);
  font-size: 13px;
}

.terminal-shell {
  overflow: hidden;
  border: 1px solid rgba(69, 230, 138, 0.28);
  border-radius: 28px;
  background:
    linear-gradient(180deg, rgba(69, 230, 138, 0.08), rgba(255, 255, 255, 0.025)),
    rgba(8, 13, 19, 0.88);
  box-shadow: 0 34px 100px rgba(0, 0, 0, 0.42);
}

.terminal-top {
  display: flex;
  height: 54px;
  align-items: center;
  gap: 9px;
  padding: 0 20px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.08);
  background: rgba(255, 255, 255, 0.035);
}

.terminal-dot {
  width: 11px;
  height: 11px;
  border-radius: 999px;
  background: #ff695c;
}

.terminal-dot:nth-child(2) {
  background: #ffd166;
}

.terminal-dot:nth-child(3) {
  background: #43ec8d;
}

.terminal-title {
  margin-left: 8px;
  color: #7890a4;
  font-size: 13px;
  font-weight: 800;
  letter-spacing: 0.02em;
}

.terminal-body {
  margin: 26px;
  padding: 24px;
  overflow-x: auto;
  border: 1px solid rgba(69, 230, 138, 0.18);
  border-radius: 20px;
  background: #020507;
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, "Liberation Mono", monospace;
  font-size: clamp(13px, 1.55vw, 16px);
  line-height: 1.78;
}

.terminal-comment {
  color: #7d9ab2;
}

.terminal-prompt {
  color: var(--v2-green);
}

.terminal-command {
  color: #f4fff8;
  font-weight: 700;
}

.terminal-spoken {
  color: #ffffff;
  font-weight: 700;
}

.terminal-success {
  color: var(--v2-cyan);
}

.terminal-result-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 18px;
  padding: 0 26px 26px;
}

.terminal-result-card {
  padding: 20px;
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 20px;
  background: rgba(255, 255, 255, 0.035);
}

.terminal-result-card strong {
  display: block;
  margin-bottom: 10px;
  color: #7890a4;
  font-size: 12px;
  letter-spacing: 0.11em;
  text-transform: uppercase;
}

.terminal-result-card h3 {
  margin: 0 0 8px;
  font-size: 1.38rem;
  letter-spacing: -0.035em;
}

.terminal-result-card p {
  margin: 0;
  color: var(--v2-muted);
  font-size: 0.94rem;
  line-height: 1.45;
}

.terminal-result-card.frontend {
  border-color: rgba(80, 215, 255, 0.24);
}

.terminal-result-card.backend {
  border-color: rgba(69, 230, 138, 0.3);
}

.studio-section {
  display: grid;
  grid-template-columns: minmax(0, 0.85fr) minmax(0, 1.15fr);
  gap: 34px;
  align-items: center;
}

.studio-lead {
  max-width: 560px;
  margin: 22px 0 0;
  color: var(--v2-soft);
  font-size: clamp(1.05rem, 1.7vw, 1.2rem);
  line-height: 1.55;
}

.studio-feature-list {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
  max-width: 620px;
  margin-top: 28px;
}

.studio-feature {
  display: flex;
  align-items: flex-start;
  gap: 10px;
  min-height: 54px;
  padding: 13px 14px;
  border: 1px solid var(--v2-line);
  border-radius: 14px;
  background: rgba(255, 255, 255, 0.035);
  color: #dbe7f1;
  font-weight: 750;
}

.studio-feature::before {
  content: "✓";
  display: grid;
  width: 22px;
  height: 22px;
  flex: 0 0 22px;
  place-items: center;
  margin-top: 1px;
  border: 2px solid var(--v2-green);
  border-radius: 999px;
  color: var(--v2-green);
  font-size: 13px;
  font-weight: 900;
}

.studio-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
  margin-top: 28px;
}

.studio-card {
  overflow: hidden;
  border: 1px solid rgba(69, 230, 138, 0.28);
  border-radius: 28px;
  background:
    linear-gradient(180deg, rgba(69, 230, 138, 0.08), rgba(255, 255, 255, 0.025)),
    rgba(8, 13, 19, 0.88);
  box-shadow: 0 34px 100px rgba(0, 0, 0, 0.42);
}

.browser-top {
  display: flex;
  height: 54px;
  align-items: center;
  gap: 9px;
  padding: 0 20px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.08);
  background: rgba(255, 255, 255, 0.035);
}

.browser-title {
  margin-left: 8px;
  color: #7890a4;
  font-size: 13px;
  font-weight: 800;
  letter-spacing: 0.02em;
}

.studio-screenshot-slot {
  position: relative;
  display: grid;
  min-height: 430px;
  place-items: center;
  margin: 26px;
  overflow: hidden;
  border: 1px dashed rgba(80, 215, 255, 0.28);
  border-radius: 22px;
  background:
    radial-gradient(circle at 50% 0%, rgba(80, 215, 255, 0.11), transparent 34%),
    radial-gradient(circle at 82% 20%, rgba(69, 230, 138, 0.1), transparent 28%),
    #05080c;
}

.studio-screenshot-slot img {
  display: block;
  width: 100%;
  height: 100%;
  min-height: 430px;
  object-fit: cover;
  object-position: top center;
}

.studio-caption {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 12px;
  padding: 0 26px 26px;
}

.caption-card {
  min-height: 88px;
  padding: 16px;
  border: 1px solid var(--v2-line);
  border-radius: 16px;
  background: rgba(255, 255, 255, 0.035);
}

.caption-card strong {
  display: block;
  margin-bottom: 6px;
  color: var(--v2-text);
  font-size: 0.96rem;
}

.caption-card span {
  color: var(--v2-muted);
  font-size: 0.86rem;
  line-height: 1.4;
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

.step-snippet,
.prompt-bubble {
  margin-top: 16px;
  padding: 10px 12px;
  border: 1px solid rgba(255, 255, 255, 0.08);
  border-radius: 8px;
  background: rgba(255, 255, 255, 0.03);
  color: #aeb7c7;
  font-family: ui-monospace, SFMono-Regular, Consolas, monospace;
  font-size: 0.8rem;
  line-height: 1.5;
}

.prompt-bubble {
  color: #d9ffe8;
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

.runners-copy,
.runners-panel {
  box-shadow: var(--v2-shadow);
}

.runners-copy {
  padding: 28px;
  border: 1px solid var(--v2-line);
  border-radius: 8px;
  background: rgba(255, 255, 255, 0.04);
}

.runners-copy h2 {
  margin: 0 0 12px;
  font-size: clamp(2rem, 4vw, 3.8rem);
  line-height: 0.98;
  letter-spacing: -0.05em;
}

.runners-copy p,
.runners-panel-header p,
.runner-item p {
  margin: 0;
  color: var(--v2-muted);
}

.runner-chip-grid {
  display: flex;
  flex-wrap: wrap;
  gap: 10px;
  margin-top: 22px;
}

.runner-chip {
  padding: 8px 12px;
  border: 1px solid rgba(69, 230, 138, 0.2);
  border-radius: 999px;
  background: rgba(69, 230, 138, 0.08);
  color: #dff7ea;
  font-size: 0.9rem;
  font-weight: 700;
}

.runners-band {
  display: grid;
  grid-template-columns: 0.9fr 1.1fr;
  gap: 18px;
  align-items: stretch;
}

.runners-panel {
  overflow: hidden;
  border: 1px solid var(--v2-line);
  border-radius: 8px;
  background: rgba(255, 255, 255, 0.04);
}

.runners-panel-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  padding: 20px;
  border-bottom: 1px solid var(--v2-line);
}

.runners-panel-header h3 {
  margin: 0;
}

.runners-list {
  display: grid;
  gap: 12px;
  padding: 20px;
}

.runner-item {
  padding: 16px 18px;
  border: 1px solid rgba(255, 255, 255, 0.08);
  border-radius: 14px;
  background: rgba(16, 18, 24, 0.96);
}

.runner-item strong {
  display: block;
  margin-bottom: 6px;
  color: var(--v2-text);
  font-size: 1.02rem;
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

.cta-actions {
  display: flex;
  align-items: center;
  justify-content: flex-end;
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

.footer {
  border-top: 1px solid var(--v2-line);
  background:
    radial-gradient(circle at 15% 0%, rgba(80, 215, 255, 0.06), transparent 26rem),
    rgba(5, 7, 10, 0.92);
}

.footer-shell {
  padding: 34px 0 28px;
}

.footer-positioning {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 10px 18px;
  margin-bottom: 30px;
  padding: 18px 20px;
  border: 1px solid rgba(69, 230, 138, 0.22);
  border-radius: 16px;
  background: linear-gradient(135deg, rgba(69, 230, 138, 0.08), rgba(80, 215, 255, 0.035));
}

.footer-positioning strong {
  color: var(--v2-text);
  font-size: clamp(1rem, 1.4vw, 1.12rem);
  letter-spacing: -0.02em;
}

.footer-positioning span {
  color: var(--v2-soft);
  font-size: 0.96rem;
}

.status-pills {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.pill {
  display: inline-flex;
  align-items: center;
  min-height: 28px;
  padding: 0 10px;
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 999px;
  background: rgba(255, 255, 255, 0.035);
  color: var(--v2-soft);
  font-size: 0.78rem;
  font-weight: 800;
  white-space: nowrap;
}

.pill.green {
  border-color: rgba(69, 230, 138, 0.28);
  background: rgba(69, 230, 138, 0.1);
  color: #d7ffe8;
}

.footer-grid {
  display: grid;
  grid-template-columns: minmax(260px, 1.2fr) repeat(3, minmax(140px, 0.55fr));
  gap: 34px;
  align-items: start;
}

.brand-row {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 12px;
}

.footer-logo {
  display: block;
  width: 38px;
  height: 38px;
}

.brand-name {
  font-weight: 900;
  letter-spacing: -0.025em;
}

.brand-copy {
  max-width: 380px;
  margin: 0;
  color: var(--v2-muted);
  line-height: 1.55;
}

.footer-column h3 {
  margin: 0 0 12px;
  color: var(--v2-text);
  font-size: 0.88rem;
  letter-spacing: 0.08em;
  text-transform: uppercase;
}

.link-list {
  display: grid;
  gap: 10px;
}

.link-list a,
.bottom-links a {
  color: var(--v2-muted);
  font-size: 0.94rem;
}

.link-list a:hover,
.bottom-links a:hover {
  color: var(--v2-text);
}

.footer-bottom {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 14px;
  margin-top: 34px;
  padding-top: 22px;
  border-top: 1px solid var(--v2-line);
  color: var(--v2-muted);
  font-size: 0.9rem;
}

.bottom-links {
  display: flex;
  flex-wrap: wrap;
  gap: 16px;
}

@media (max-width: 980px) {
  .hero-grid,
  .featured-shelf,
  .ai-workflow-grid,
  .install-workflow,
  .studio-section,
  .runners-band,
  .cta-box {
    grid-template-columns: 1fr;
  }

  .featured-stage {
    border-right: 0;
    border-bottom: 1px solid rgba(255, 255, 255, 0.08);
  }

  .featured-previews {
    grid-template-columns: repeat(2, 1fr);
    grid-template-rows: 1fr;
  }

  .featured-image-button img {
    max-height: none;
  }

  h1 {
    max-width: 690px;
  }

  .steps,
  .nav-links {
    display: none;
  }

  .architecture-card {
    padding: 22px;
  }

  .architecture-intro {
    grid-template-columns: 1fr;
    align-items: start;
  }

  .path-cards {
    grid-template-columns: 1fr;
  }

  .terminal-body {
    margin: 18px;
    padding: 18px;
  }

  .terminal-result-grid {
    grid-template-columns: 1fr;
    padding: 0 18px 18px;
  }

  .studio-feature-list {
    grid-template-columns: 1fr;
  }

  .studio-screenshot-slot {
    min-height: 300px;
    margin: 18px;
  }

  .studio-screenshot-slot img {
    min-height: 300px;
  }

  .studio-caption {
    grid-template-columns: 1fr;
    padding: 0 18px 18px;
  }

  .architecture-diagram {
    display: none;
  }

  .mobile-diagram {
    display: grid;
    gap: 18px;
  }

  .footer-grid {
    grid-template-columns: 1fr 1fr;
  }

  .brand-column {
    grid-column: 1 / -1;
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

  .prompt-strip,
  .runners-panel-header {
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

  .featured-previews {
    grid-template-columns: 1fr;
    grid-template-rows: repeat(2, minmax(0, 1fr));
    min-height: unset;
    height: auto;
  }

  .featured-image-button {
    padding: 0 12px 0;
  }

  .featured-image-button img {
    max-height: none;
  }

  .featured-meta {
    gap: 12px;
    padding: 12px 16px 6px;
  }

  .carousel-frame,
  .featured-shelf,
  .featured-stage {
    min-height: 0;
    height: auto;
  }

  .architecture-card {
    padding: 18px;
    border-radius: 22px;
  }

  .steps,
  .templates-grid,
  .live-strip,
  .path-cards {
    grid-template-columns: 1fr;
  }

  .section-head {
    display: grid;
  }

  .footer-positioning {
    align-items: flex-start;
  }

  .footer-grid {
    grid-template-columns: 1fr;
  }

  .footer-bottom {
    display: grid;
    justify-content: start;
  }
}
</style>
