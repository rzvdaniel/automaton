<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
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

const activeDescription = computed(() => {
  const app = activeApp.value
  if (!app) return ''

  return app.summary || app.description || 'Open the detail page to explore the app.'
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

function viewApp(app?: MarketplaceAppCard) {
  if (!app) return
  emit('select', app.id)
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
      <div class="feature-layout">
        <div class="feature-media">
          <div class="feature-media-frame">
            <img v-if="imageUrl(activeApp)" :src="imageUrl(activeApp)" :alt="activeApp.name" />
            <div v-else class="feature-fallback">{{ activeApp.name.charAt(0) }}</div>
          </div>
          <div class="feature-media-badges">
            <span class="badge badge-live">Live app</span>
          </div>
        </div>

        <div class="feature-content">
          <div class="feature-copy">
            <div class="badge-row">
              <span v-if="activeApp.isFeatured" class="badge badge-hot">Featured</span>
            </div>

            <h3>{{ activeApp.name }}</h3>
            <p class="feature-description">{{ activeDescription }}</p>
            <div class="creator-line">{{ activeMeta }}</div>
          </div>

          <div class="feature-actions feature-actions-single">
            <button class="showcase-button primary" type="button" @click.stop="viewApp(activeApp)">
              <i class="pi pi-arrow-right"></i>
              View details
            </button>
          </div>
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
  border-radius: 20px;
  background: linear-gradient(180deg, rgba(14, 20, 28, 0.98), rgba(9, 13, 19, 0.98));
  cursor: pointer;
}

.feature-card {
  display: block;
  height: 100%;
  min-height: 0;
  overflow: hidden;
  box-shadow: 0 24px 80px rgba(0, 0, 0, 0.36);
  transition: border-color 0.22s ease, box-shadow 0.22s ease;
}

.feature-layout {
  position: relative;
  z-index: 1;
  display: grid;
  grid-template-columns: minmax(0, 1.7fr) minmax(320px, 0.95fr);
  height: 100%;
  min-height: 0;
}

.feature-layout::before {
  content: "";
  position: absolute;
  inset: 0;
  background: rgba(0, 0, 0, 0.28);
  pointer-events: none;
}

.feature-layout::after {
  content: "";
  position: absolute;
  inset: 0;
  opacity: 0.92;
  background:
    linear-gradient(
      to right,
      rgba(0, 0, 0, 0.02) 16%,
      rgba(0, 0, 0, 0.14) 34%,
      rgba(0, 0, 0, 0.52) 56%,
      rgba(0, 0, 0, 0.84) 78%,
      rgba(0, 0, 0, 0.94) 100%
    ),
    linear-gradient(
      to top,
      rgba(0, 0, 0, 0.42) 0%,
      rgba(0, 0, 0, 0.16) 22%,
      rgba(0, 0, 0, 0) 40%
    );
  pointer-events: none;
  transition: opacity 0.22s ease;
}

.feature-media {
  position: relative;
  display: grid;
  align-items: center;
  min-width: 0;
  min-height: 0;
  padding: 10px 6px 10px 10px;
}

.feature-media-frame {
  width: 100%;
  height: 100%;
  min-height: 100%;
  aspect-ratio: 16 / 10;
  align-self: center;
}

.feature-card img,
.feature-fallback {
  width: 100%;
  height: 100%;
  object-fit: cover;
  object-position: center center;
  background: #080b10;
  opacity: 1;
  border-radius: 16px;
  border: 1px solid rgba(255, 255, 255, 0.08);
}

.feature-media-badges {
  position: absolute;
  left: 32px;
  bottom: 14px;
  display: flex;
  gap: 8px;
  pointer-events: none;
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
  position: relative;
  z-index: 2;
  display: flex;
  flex-direction: column;
  justify-content: center;
  gap: 20px;
  min-width: 0;
  align-items: flex-start;
  padding: 54px 28px 30px 22px;
  min-height: 100%;
}

.feature-copy {
  width: min(100%, 22rem);
  min-width: 0;
}

.feature-content h3 {
  margin: 0 0 10px;
  max-width: 10ch;
  color: #f5f7fb;
  font-size: clamp(1.9rem, 2.8vw, 2.7rem);
  font-weight: 800;
  line-height: 0.94;
  letter-spacing: -0.05em;
  text-wrap: balance;
  text-shadow: 0 8px 22px rgba(0, 0, 0, 0.32);
}

.badge-row {
  display: flex;
  flex-wrap: wrap;
  gap: 7px;
  margin-bottom: 12px;
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
  margin-top: 14px;
  color: #dce3ee;
  font-size: 0.94rem;
  text-shadow: 0 4px 12px rgba(0, 0, 0, 0.24);
}

.feature-description {
  max-width: 34ch;
  margin: 0;
  color: #c2cbda;
  font-size: 0.97rem;
  line-height: 1.55;
  text-shadow: 0 4px 12px rgba(0, 0, 0, 0.24);
}

.feature-actions {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 10px;
  min-width: 0;
  width: 100%;
  align-self: end;
}

.feature-actions-single {
  grid-template-columns: minmax(200px, 1fr);
}

.feature-actions .showcase-button.primary {
  grid-column: 1 / -1;
}

.showcase-button {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 7px;
  min-height: 46px;
  min-width: 0;
  padding: 0 16px;
  border: 1px solid rgba(255, 255, 255, 0.12);
  border-radius: 10px;
  background: #11161d;
  color: #fff;
  font-size: 0.88rem;
  font-weight: 800;
  line-height: 1.15;
  white-space: normal;
  text-align: center;
  cursor: pointer;
  transition: transform 0.18s ease, border-color 0.18s ease, background 0.18s ease;
}

.showcase-button:hover {
  transform: translateY(-1px);
  border-color: rgba(255, 255, 255, 0.2);
}

.feature-card:hover {
  border-color: rgba(80, 215, 255, 0.24);
  box-shadow: 0 28px 88px rgba(0, 0, 0, 0.42);
}

.feature-card:hover .feature-layout::after {
  opacity: 1;
}

.feature-card:hover .feature-card img {
  transform: scale(1.03);
}

.feature-card img {
  transition: transform 0.28s ease;
}

.showcase-button.primary {
  border-color: rgba(69, 230, 138, 0.55);
  background: #45e68a;
  color: #041008;
  min-width: 200px;
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

  .feature-card {
    display: grid;
  }

  .feature-layout {
    grid-template-columns: 1fr;
  }

  .feature-layout::before,
  .feature-layout::after {
    display: none;
  }

  .feature-content {
    display: flex;
    flex-direction: column;
    justify-content: flex-start;
    align-items: stretch;
    padding: 8px 22px 22px;
    min-height: 0;
  }

  .feature-copy {
    width: 100%;
  }

  .feature-content h3 {
    max-width: none;
  }

  .feature-actions {
    min-width: 0;
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

  .feature-card {
    border-radius: 18px;
  }

  .feature-media {
    padding: 14px;
  }

  .feature-media-frame {
    min-height: 0;
    height: auto;
    aspect-ratio: 16 / 9;
  }

  .feature-media-badges {
    left: 24px;
    bottom: 10px;
  }

  .feature-content {
    gap: 18px;
    padding: 4px 14px 16px;
  }

  .feature-content h3 {
    font-size: clamp(1.9rem, 8vw, 2.5rem);
    line-height: 0.98;
  }

  .feature-description {
    font-size: 0.95rem;
    line-height: 1.55;
  }

  .feature-actions {
    grid-template-columns: 1fr 1fr;
  }
}
</style>
