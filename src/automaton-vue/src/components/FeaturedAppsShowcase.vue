<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { publicAppConfig } from '@/config/publicAppConfig'
import { resolveMarketplaceAssetUrl } from '@/composables/useMarketplaceApps'
import type { MarketplaceAppCard } from '@/types/marketplace'

const props = defineProps<{
  apps: MarketplaceAppCard[]
}>()

const emit = defineEmits<{
  (e: 'select', appId: string): void
}>()

const activeIndex = ref(0)
const isPaused = ref(false)
let rotationId: number | undefined

const activeApp = computed(() => props.apps[activeIndex.value])

const sideApps = computed(() => {
  if (props.apps.length <= 1) return []

  return [1, 2]
    .map((offset) => props.apps[(activeIndex.value + offset) % props.apps.length])
    .filter((app): app is MarketplaceAppCard => Boolean(app))
})

const activeCreatorName = computed(() => creatorName(activeApp.value))

const activeCreatorInitials = computed(() => creatorInitials(activeApp.value))

const activeMeta = computed(() => {
  const app = activeApp.value
  if (!app) return ''

  const parts = [`by ${activeCreatorName.value}`, `${formatCount(app.viewCount)} views`]
  return parts.join(' · ')
})

function creatorName(app?: MarketplaceAppCard) {
  return app?.creatorDisplayName || app?.creatorPublicUsername || 'Automaton creator'
}

function creatorInitials(app?: MarketplaceAppCard) {
  return creatorName(app)
    .split(/\s+/)
    .map((part) => part.charAt(0))
    .join('')
    .slice(0, 2)
    .toUpperCase()
}

function imageUrl(app?: MarketplaceAppCard) {
  return app?.thumbnailUrl ? resolveMarketplaceAssetUrl(app.thumbnailUrl) : ''
}

function formatCount(value: number) {
  if (value >= 1000) return `${(value / 1000).toFixed(value >= 10000 ? 0 : 1)}k`
  return String(value)
}

function setActive(index: number) {
  activeIndex.value = index
}

function next() {
  if (props.apps.length <= 1) return
  activeIndex.value = (activeIndex.value + 1) % props.apps.length
}

function previous() {
  if (props.apps.length <= 1) return
  activeIndex.value = (activeIndex.value - 1 + props.apps.length) % props.apps.length
}

function selectApp(app?: MarketplaceAppCard) {
  if (!app) return
  emit('select', app.id)
}

function playApp(app?: MarketplaceAppCard) {
  if (!app) return

  if (app.publicUrl) {
    window.open(`${publicAppConfig.studioUrl}web/${app.publicUrl}`, '_blank', 'noopener')
    return
  }

  emit('select', app.id)
}

function forkApp(app?: MarketplaceAppCard) {
  if (!app) return
  window.open(`${publicAppConfig.studioUrl}apps/fork/${app.id}`, '_blank', 'noopener')
}

function startRotation() {
  stopRotation()
  rotationId = window.setInterval(() => {
    if (!isPaused.value) {
      next()
    }
  }, 6500)
}

function stopRotation() {
  if (rotationId !== undefined) {
    window.clearInterval(rotationId)
    rotationId = undefined
  }
}

watch(
  () => props.apps.length,
  (length) => {
    if (activeIndex.value >= length) {
      activeIndex.value = 0
    }
  },
)

onMounted(startRotation)
onBeforeUnmount(stopRotation)
</script>

<template>
  <div
    v-if="activeApp"
    class="featured-showcase"
    @mouseenter="isPaused = true"
    @mouseleave="isPaused = false"
    @focusin="isPaused = true"
    @focusout="isPaused = false"
  >
    <article
      class="feature-card"
      role="link"
      tabindex="0"
      @click="selectApp(activeApp)"
      @keydown.enter.prevent="selectApp(activeApp)"
      @keydown.space.prevent="selectApp(activeApp)"
    >
      <img v-if="imageUrl(activeApp)" :src="imageUrl(activeApp)" :alt="activeApp.name" />
      <div v-else class="feature-fallback">{{ activeApp.name.charAt(0) }}</div>

      <div class="feature-content">
        <div class="badge-row">
          <span class="badge badge-live">Live app</span>
          <span v-if="activeApp.isFeatured" class="badge badge-hot">Featured</span>
        </div>

        <h3>{{ activeApp.name }}</h3>
        <div class="creator-line">{{ activeMeta }}</div>

        <div class="feature-actions">
          <button class="showcase-button primary" type="button" @click.stop="playApp(activeApp)">
            <i class="pi pi-play"></i>
            Play
          </button>
          <button v-if="activeApp.isForkable" class="showcase-button" type="button" @click.stop="forkApp(activeApp)">
            <i class="pi pi-copy"></i>
            Fork
          </button>
          <button class="showcase-button" type="button" @click.stop="selectApp(activeApp)">
            <i class="pi pi-sparkles"></i>
            Build similar
          </button>
        </div>
      </div>
    </article>

    <div class="side-stack" aria-label="More featured apps">
      <article
        v-for="app in sideApps"
        :key="app.id"
        class="mini-feature"
        role="button"
        tabindex="0"
        @click="setActive(apps.findIndex((candidate) => candidate.id === app.id))"
        @keydown.enter.prevent="setActive(apps.findIndex((candidate) => candidate.id === app.id))"
        @keydown.space.prevent="setActive(apps.findIndex((candidate) => candidate.id === app.id))"
      >
        <img v-if="imageUrl(app)" :src="imageUrl(app)" :alt="app.name" />
        <div v-else class="mini-feature-fallback">{{ app.name.charAt(0) }}</div>
        <div class="mini-feature-content">
          <h4>{{ app.name }}</h4>
          <p>{{ app.summary || app.description || 'Published Automaton app' }}</p>
        </div>
      </article>
    </div>

    <div v-if="apps.length > 1" class="showcase-controls" aria-label="Featured app controls">
      <button
        class="showcase-arrow"
        type="button"
        aria-label="Previous featured app"
        @click="previous"
      >
        <i class="pi pi-chevron-left"></i>
      </button>

      <div class="showcase-dots" aria-label="Featured app slides">
        <button
          v-for="(app, index) in apps"
          :key="app.id"
          class="showcase-dot"
          :class="{ active: index === activeIndex }"
          type="button"
          :aria-label="`Show ${app.name}`"
          @click="setActive(index)"
        ></button>
      </div>

      <button
        class="showcase-arrow"
        type="button"
        aria-label="Next featured app"
        @click="next"
      >
        <i class="pi pi-chevron-right"></i>
      </button>
    </div>
  </div>
</template>

<style scoped>
.featured-showcase {
  display: grid;
  grid-template-columns: minmax(0, 1.2fr) minmax(280px, 0.58fr);
  grid-template-rows: 530px;
  gap: 16px;
  align-items: stretch;
}

.feature-card,
.mini-feature {
  position: relative;
  overflow: hidden;
  border: 1px solid rgba(255, 255, 255, 0.16);
  border-radius: 8px;
  background: #111924;
  cursor: pointer;
}

.feature-card {
  height: 100%;
  min-height: 0;
  box-shadow: 0 24px 80px rgba(0, 0, 0, 0.36);
}

.feature-card::after,
.mini-feature::after {
  content: "";
  position: absolute;
  inset: auto 0 0;
  height: 66%;
  background: linear-gradient(180deg, transparent, rgba(5, 7, 10, 0.04) 54%, rgba(5, 7, 10, 0.14));
  pointer-events: none;
}

.feature-card img,
.feature-fallback {
  width: 100%;
  height: 100%;
  min-height: 100%;
  object-fit: contain;
  background: #080b10;
  opacity: 1;
}

.feature-fallback,
.mini-feature-fallback {
  display: grid;
  place-items: center;
  color: #50d7ff;
  font-size: 4rem;
  font-weight: 900;
}

.feature-content {
  position: absolute;
  z-index: 2;
  inset: auto 0 0 0;
  padding: 26px;
}

.feature-content h3 {
  margin: 0 0 10px;
  max-width: 720px;
  color: #f5f7fb;
  font-size: clamp(2.8rem, 4.6vw, 4rem);
  font-weight: 800;
  line-height: 0.92;
  letter-spacing: -0.06em;
  text-shadow: 0 10px 28px rgba(0, 0, 0, 0.55);
  text-wrap: balance;
}

.badge-row {
  display: flex;
  flex-wrap: wrap;
  gap: 7px;
}

.badge {
  padding: 4px 8px;
  border-radius: 999px;
  background: rgba(80, 215, 255, 0.12);
  color: #bdefff;
  font-size: 0.68rem;
  font-weight: 950;
  letter-spacing: 0.045em;
  line-height: 1;
  text-transform: uppercase;
}

.badge-live {
  padding: 5px 9px;
  background: rgba(69, 230, 138, 0.16);
  color: #45e68a;
}

.badge-static {
  color: #d6dbe7;
  background: rgba(255, 255, 255, 0.08);
}

.badge-hot {
  color: #ffcb57;
  background: rgba(255, 203, 87, 0.13);
}

.creator-line {
  margin-bottom: 18px;
  color: #d4d9e2;
  font-size: 0.96rem;
  text-shadow: 0 6px 18px rgba(0, 0, 0, 0.5);
}

.feature-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-top: 12px;
}

.showcase-button {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 7px;
  min-height: 46px;
  padding: 0 20px;
  border: 1px solid rgba(255, 255, 255, 0.12);
  border-radius: 10px;
  background: #11161d;
  color: #fff;
  font-size: 0.92rem;
  font-weight: 800;
  cursor: pointer;
}

.showcase-button.primary {
  border-color: rgba(69, 230, 138, 0.55);
  background: #45e68a;
  color: #041008;
}

.side-stack {
  display: grid;
  height: 100%;
  grid-template-rows: 1fr 1fr;
  gap: 14px;
}

.mini-feature {
  min-height: 0;
  border-color: rgba(255, 255, 255, 0.1);
  background: rgba(255, 255, 255, 0.04);
}

.mini-feature img,
.mini-feature-fallback {
  width: 100%;
  height: 155px;
  object-fit: cover;
  opacity: 0.9;
}

.mini-feature-content {
  position: static;
  padding: 16px;
}

.mini-feature h4 {
  margin: 0 0 6px;
  color: #f5f7fb;
  font-size: 1.2rem;
  line-height: 1.1;
}

.mini-feature p {
  margin: 0;
  color: #97a1b3;
  font-size: 0.9rem;
}

.showcase-controls {
  grid-column: 1 / -1;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 10px;
  margin-top: -2px;
}

.showcase-arrow {
  display: grid;
  width: 36px;
  height: 36px;
  place-items: center;
  border: 1px solid rgba(255, 255, 255, 0.08);
  background: rgba(22, 36, 50, 0.78);
  border-radius: 999px;
  color: #ffffff;
  cursor: pointer;
}

.showcase-dots {
  display: flex;
  align-items: center;
  gap: 6px;
}

.showcase-dot {
  width: 16px;
  height: 9px;
  border: 0;
  border-radius: 999px;
  background: rgba(143, 152, 160, 0.55);
  cursor: pointer;
}

.showcase-dot.active {
  background: #50d7ff;
}

@media (max-width: 900px) {
  .featured-showcase {
    grid-template-columns: 1fr;
    grid-template-rows: auto;
  }

  .side-stack {
    grid-template-columns: 1fr 1fr;
    height: auto;
    grid-template-rows: none;
  }
}

@media (max-width: 620px) {
  .side-stack {
    grid-template-columns: 1fr;
  }

  .feature-card,
  .feature-fallback {
    min-height: 310px;
  }

  .feature-card img,
  .feature-fallback {
    height: 310px;
  }

  .feature-content {
    padding: 18px 14px;
  }

  .feature-content h3 {
    font-size: 3rem;
  }
}
</style>
