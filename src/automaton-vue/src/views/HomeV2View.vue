<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import PublicSiteHeader from '@/components/PublicSiteHeader.vue'
import { publicAppConfig } from '@/config/publicAppConfig'
import { resolveMarketplaceAssetUrl, useMarketplaceApps } from '@/composables/useMarketplaceApps'
import type { MarketplaceAppCard } from '@/types/marketplace'
import studioScreenshotUrl from '@/assets/studio-apps-screenshot.jpg'

const router = useRouter()
const { featuredApps, loadApps } = useMarketplaceApps(12)
const activeIndex = ref(0)
const isCarouselPaused = ref(false)
const promptCopied = ref(false)
let rotationId: number | undefined
let promptResetId: number | undefined

const aiStarterPrompt = `Build a [describe your app] as an Automaton Monster app.

Read https://automaton.monster/llms.txt first.

Use the Automaton CLI as the primary path. If the CLI is missing, install it using the official instructions. Run \`automaton login\` and let me paste my Automaton API key when prompted.

For a new app, run \`automaton new "[app-name]"\` before creating app files. Then follow the generated local guidance in \`AGENTS.md\` and \`.automaton/agents/\`.

Default to a frontend-only Vite app unless backend behavior is truly required. Deploy with \`automaton deploy\`. Do not publish publicly until I explicitly approve.`

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
              <span>Run your backend</span>
              <span>anywhere.</span>
            </h1>
            <p class="hero-copy">
              Use Claude Code, Codex, Cursor, Copilot, or Gemini. Automaton gives your agent the deploy path: frontend hosting is included, and your backend runs on your runner.
            </p>
            <div class="hero-actions">
              <RouterLink class="button primary" to="/docs/getting-started">Get Started</RouterLink>
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
