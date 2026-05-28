<script setup lang="ts">
import { computed, onMounted } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import MarketplaceCarousel from '@/components/MarketplaceCarousel.vue'
import { useMarketplaceApps } from '@/composables/useMarketplaceApps'

const router = useRouter()
const { featuredApps, loading, loadApps } = useMarketplaceApps(10)
const heroApps = computed(() => {
  const featured = featuredApps.value.slice(0, 6)
  return featured
})

function openDetails(appId: string) {
  void router.push(`/apps/${appId}`)
}

onMounted(() => {
  void loadApps()
})
</script>

<template>
  <section class="hero">
    <!-- Background glow -->
    <div class="hero-glow" aria-hidden="true" />

    <div class="container hero-inner">
      <!-- Left: text -->
      <div class="hero-text">
        <h1 class="hero-title">
          <span class="hero-title-line">AI can code.</span><br />
          <span class="hero-accent">Automaton helps it ship.</span>
        </h1>

        <p class="hero-subtitle">
          Install the CLI once, run setup, then ask your AI coding agent for the app you want.
          Automaton gives it the local guidance and deploy path to ship real software.
        </p>

        <div class="hero-actions">
          <RouterLink to="/cli" class="btn btn-primary btn-lg">
            <i class="pi pi-download" />
            Install CLI
          </RouterLink>
          <RouterLink to="/cli#downloads" class="btn btn-secondary btn-lg">
            Download builds
            <i class="pi pi-arrow-right" />
          </RouterLink>
          <a href="/apps" class="btn btn-secondary btn-lg">
            Explore apps
            <i class="pi pi-arrow-right" />
          </a>
        </div>

        <div class="hero-tools">
          <span class="tools-label">Works with</span>
          <div class="tools-list">
            <span class="tool-badge"><i class="pi pi-github" /> GitHub Copilot</span>
            <span class="tool-badge"><i class="pi pi-sparkles" /> Claude</span>
            <span class="tool-badge"><i class="pi pi-box" /> Cursor</span>
            <span class="tool-badge"><i class="pi pi-code" /> Any IDE</span>
          </div>
        </div>

        <p class="hero-no-lock"><span class="hero-check">✓</span> No lock-in. Run locally or in the cloud.</p>
      </div>

      <!-- Right: featured apps -->
      <div class="hero-showcase">
        <MarketplaceCarousel
          v-if="heroApps.length > 0"
          :apps="heroApps"
          variant="hero"
          compact
          title="Featured apps"
          @select="openDetails"
        />

        <div v-else class="hero-preview-fallback" :class="{ 'hero-preview-loading': loading }">
          <div class="preview-window preview-window-main">
            <div class="preview-bar">
              <span class="preview-dot" />
              <span class="preview-dot" />
              <span class="preview-dot" />
            </div>
            <div class="preview-body">
              <span class="preview-badge">Live app</span>
              <h3>{{ loading ? 'Loading featured apps...' : 'Featured app previews' }}</h3>
              <p>Admin-featured apps and screenshots appear here when the marketplace is available.</p>
            </div>
          </div>
        </div>

        <div class="hero-setup-strip">
          <div><span>$</span> dotnet tool install -g Automaton.Cli</div>
          <div><span>$</span> automaton setup</div>
          <div class="hero-setup-prompt">Then ask: Build a live ISS tracker with Earth view and astronaut telemetry.</div>
        </div>

        <div class="hero-proof-grid" aria-label="Automaton capabilities">
          <div><strong>Live apps</strong><span>from Admin featured picks</span></div>
          <div><strong>Templates</strong><span>ready for remix flows</span></div>
          <div><strong>Self-host</strong><span>local or cloud runners</span></div>
        </div>
      </div>
    </div>
  </section>
</template>

<style scoped>
.hero {
  position: relative;
  padding: 48px 0 36px;
  overflow: hidden;
  background:
    radial-gradient(circle at 18% 0%, rgba(80, 215, 255, 0.12), transparent 32rem),
    radial-gradient(circle at 82% 8%, rgba(69, 230, 138, 0.1), transparent 30rem),
    linear-gradient(rgba(80, 215, 255, 0.04) 1px, transparent 1px),
    linear-gradient(90deg, rgba(69, 230, 138, 0.035) 1px, transparent 1px);
  background-size: auto, auto, 48px 48px, 48px 48px;
}

.hero-glow {
  position: absolute;
  top: -100px;
  right: -100px;
  width: 700px;
  height: 600px;
  background: radial-gradient(ellipse, rgba(80, 215, 255, 0.16) 0%, transparent 65%);
  pointer-events: none;
}

.hero-inner {
  display: grid;
  grid-template-columns: minmax(0, 0.9fr) minmax(430px, 1.1fr);
  gap: 44px;
  align-items: center;
}

.hero-text {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  text-align: left;
}


.hero-title {
  font-size: clamp(2.2rem, 4.5vw, 3.4rem);
  font-weight: 800;
  line-height: 1.1;
  letter-spacing: -0.03em;
  color: var(--color-text);
  max-width: 700px;
}

.hero-title-line {
  white-space: nowrap;
}

.hero-accent {
  background: linear-gradient(135deg, #45e68a, #50d7ff);
  -webkit-background-clip: text;
  -webkit-text-fill-color: transparent;
  background-clip: text;
}

.hero-subtitle {
  font-size: clamp(1rem, 2vw, 1.15rem);
  color: var(--color-text-muted);
  max-width: 560px;
  margin-top: 16px;
  line-height: 1.7;
}

.hero-actions {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-top: 28px;
  flex-wrap: wrap;
}

.btn-lg {
  padding: 10px 18px;
  font-size: 0.85rem;
}

.hero-no-lock {
  margin-top: 16px;
  font-size: 0.82rem;
  color: var(--color-text-muted);
  font-weight: 500;
}

.hero-showcase {
  position: relative;
  min-width: 0;
}

.hero-showcase::before {
  content: '';
  position: absolute;
  inset: -32px -22px auto auto;
  z-index: -1;
  width: 78%;
  height: 72%;
  border-radius: 18px;
  background: linear-gradient(135deg, rgba(80, 215, 255, 0.2), rgba(69, 230, 138, 0.12));
  filter: blur(12px);
}

.hero-setup-strip {
  display: grid;
  gap: 7px;
  margin-top: 14px;
  padding: 14px 16px;
  border: 1px solid rgba(69, 230, 138, 0.22);
  border-radius: 8px;
  background: rgba(7, 8, 11, 0.76);
  color: #d9ffe8;
  font-family: var(--font-mono);
  font-size: 0.82rem;
  box-shadow: 0 18px 50px rgba(0, 0, 0, 0.28);
}

.hero-setup-strip span {
  color: var(--color-green);
  font-weight: 800;
}

.hero-setup-prompt {
  color: #8b95a8;
}

.hero-proof-grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 10px;
  margin-top: 12px;
}

.hero-proof-grid div {
  padding: 12px;
  border: 1px solid var(--color-border);
  border-radius: 8px;
  background: rgba(255, 255, 255, 0.035);
}

.hero-proof-grid strong {
  display: block;
  color: var(--color-text);
  font-size: 0.92rem;
  line-height: 1.1;
}

.hero-proof-grid span {
  display: block;
  margin-top: 4px;
  color: var(--color-text-muted);
  font-size: 0.76rem;
}

.hero-preview-fallback {
  min-height: 360px;
  padding: 14px;
  border: 1px solid rgba(255, 255, 255, 0.12);
  border-radius: 8px;
  background: rgba(15, 18, 24, 0.94);
  box-shadow: 0 24px 80px rgba(0, 0, 0, 0.38);
}

.preview-window {
  height: 100%;
  min-height: 330px;
  overflow: hidden;
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 8px;
  background: #172231;
}

.preview-bar {
  display: flex;
  gap: 6px;
  padding: 12px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.08);
  background: #080b10;
}

.preview-dot {
  width: 10px;
  height: 10px;
  border-radius: 999px;
  background: rgba(255, 255, 255, 0.24);
}

.preview-body {
  display: grid;
  place-content: center;
  min-height: 280px;
  padding: 28px;
  text-align: center;
}

.preview-badge {
  justify-self: center;
  margin-bottom: 12px;
  padding: 5px 9px;
  border-radius: 999px;
  background: var(--color-green-soft);
  color: var(--color-green);
  font-size: 0.72rem;
  font-weight: 800;
  letter-spacing: 0.05em;
  text-transform: uppercase;
}

.preview-body h3 {
  margin: 0 0 8px;
  font-size: 1.5rem;
}

.preview-body p {
  margin: 0;
  color: var(--color-text-muted);
}

.hero-check {
  color: var(--color-green);
}

.hero-tools {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-top: 28px;
  flex-wrap: wrap;
  justify-content: center;
}

.tools-label {
  font-size: 0.8rem;
  color: var(--color-text-muted);
  font-weight: 500;
}

.tools-list {
  display: flex;
  align-items: center;
  gap: 18px;
  flex-wrap: wrap;
}

.tool-badge {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  font-size: 0.78rem;
  font-weight: 500;
  color: var(--color-text-muted);
}

.tool-badge i {
  font-size: 0.88rem;
  color: rgba(233, 236, 245, 0.72);
}

@media (max-width: 900px) {
  .hero-inner {
    grid-template-columns: 1fr;
    gap: 20px;
  }

  .hero-text {
    align-items: center;
    text-align: center;
  }

  .hero-actions {
    justify-content: center;
  }

  .hero-tools {
    justify-content: center;
  }

  .hero-showcase {
    width: 100%;
    max-width: 760px;
    margin: 0 auto;
  }
}

@media (max-width: 620px) {
  .hero {
    padding-top: 34px;
  }

  .hero-proof-grid {
    grid-template-columns: 1fr;
  }

  .hero-setup-strip {
    font-size: 0.76rem;
  }
}
</style>
