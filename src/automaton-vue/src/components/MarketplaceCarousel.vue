<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { publicAppConfig } from '@/config/publicAppConfig'
import type { MarketplaceAppCard } from '@/types/marketplace'

const props = withDefaults(
  defineProps<{
    apps: MarketplaceAppCard[]
    compact?: boolean
    title?: string
    variant?: 'showcase' | 'hero'
  }>(),
  {
    compact: false,
    title: 'Featured & Recommended',
    variant: 'showcase',
  },
)

const emit = defineEmits<{
  (e: 'select', appId: string): void
}>()

const router = useRouter()
const activeIndex = ref(0)
const hoveredPreviewUrl = ref<string | null>(null)

const activeApp = computed(() => props.apps[activeIndex.value])

const activeScreenshots = computed(() => {
  const app = activeApp.value
  if (!app) return []

  const screenshots = (app.screenshots ?? []).slice(0, 4).map((screenshot) => ({
    key: screenshot.id,
    imageUrl: screenshot.imageUrl,
    label: screenshot.caption || app.name,
  }))

  if (screenshots.length > 0) return screenshots

  return app.thumbnailUrl
    ? [
        {
          key: 'thumbnail',
          imageUrl: app.thumbnailUrl,
          label: app.name,
        },
      ]
    : []
})

const mainPreviewUrl = computed(() => {
  if (hoveredPreviewUrl.value) return hoveredPreviewUrl.value
  if (activeApp.value?.thumbnailUrl) return activeApp.value.thumbnailUrl
  return activeScreenshots.value[0]?.imageUrl ?? ''
})

watch(
  () => props.apps.length,
  () => {
    activeIndex.value = 0
    hoveredPreviewUrl.value = null
  },
)

function resolveAssetUrl(url?: string | null) {
  if (!url) return ''
  if (url.startsWith('http')) return url
  return `${publicAppConfig.apiBaseUrl}${url.replace(/^\/+/, '')}`
}

function previous() {
  if (props.apps.length === 0) return
  activeIndex.value = (activeIndex.value - 1 + props.apps.length) % props.apps.length
  hoveredPreviewUrl.value = null
}

function next() {
  if (props.apps.length === 0) return
  activeIndex.value = (activeIndex.value + 1) % props.apps.length
  hoveredPreviewUrl.value = null
}

function setActiveSlide(index: number) {
  activeIndex.value = index
  hoveredPreviewUrl.value = null
}

function setActivePreview(url: string) {
  hoveredPreviewUrl.value = url
}

function clearActivePreview() {
  hoveredPreviewUrl.value = null
}

function openActiveApp() {
  if (!activeApp.value) return
  emit('select', activeApp.value.id)
}

function openCreator(publicUsername: string) {
  void router.push(`/${publicUsername}`)
}
</script>

<template>
  <div
    v-if="activeApp"
    class="market-carousel"
    :class="[
      `market-carousel-${variant}`,
      {
        'market-carousel-compact': compact,
      },
    ]"
  >
    <span class="section-label carousel-heading">{{ title }}</span>

    <button
      v-if="apps.length > 1"
      class="carousel-arrow carousel-arrow-left"
      type="button"
      aria-label="Previous featured app"
      @click="previous"
    >
      <i class="pi pi-chevron-left"></i>
    </button>

    <article
      class="carousel-slide"
      role="button"
      tabindex="0"
      @click="openActiveApp"
      @keydown.enter.prevent="openActiveApp"
      @keydown.space.prevent="openActiveApp"
      @mouseleave="clearActivePreview"
    >
      <div class="carousel-main">
        <div class="carousel-main-media">
          <img
            v-if="mainPreviewUrl"
            :src="resolveAssetUrl(mainPreviewUrl)"
            :alt="activeApp.name"
          />
          <div v-else class="carousel-placeholder">{{ activeApp.name.slice(0, 1) }}</div>
        </div>
      </div>

      <aside class="carousel-screenshots">
        <h3>{{ activeApp.name }}</h3>
        <button
          v-if="activeApp.creatorPublicUsername"
          type="button"
          class="carousel-creator-link"
          @click.stop="openCreator(activeApp.creatorPublicUsername)"
        >
          by {{ activeApp.creatorDisplayName || activeApp.creatorPublicUsername }}
        </button>
        <div v-if="activeScreenshots.length > 0" class="screenshot-grid">
          <button
            v-for="screenshot in activeScreenshots"
            :key="screenshot.key"
            class="screenshot-tile"
            type="button"
            :aria-label="`Preview ${screenshot.label}`"
            @click.stop="setActivePreview(screenshot.imageUrl)"
            @focus="setActivePreview(screenshot.imageUrl)"
            @mouseenter="setActivePreview(screenshot.imageUrl)"
          >
            <img :src="resolveAssetUrl(screenshot.imageUrl)" :alt="screenshot.label" />
          </button>
        </div>

        <div class="carousel-copy">
          <p>{{ activeApp.description || activeApp.summary || 'Published Automaton app' }}</p>
          <div class="carousel-stats">
            <span v-if="activeApp.category" class="carousel-tag">{{ activeApp.category }}</span>
            <span><i class="pi pi-star-fill"></i>{{ activeApp.starCount }}</span>
            <span><i class="pi pi-eye"></i>{{ activeApp.viewCount }}</span>
          </div>
        </div>
      </aside>
    </article>

    <button
      v-if="apps.length > 1"
      class="carousel-arrow carousel-arrow-right"
      type="button"
      aria-label="Next featured app"
      @click="next"
    >
      <i class="pi pi-chevron-right"></i>
    </button>

    <div v-if="apps.length > 1" class="carousel-dots" aria-label="Featured app slides">
      <button
        v-for="(app, index) in apps"
        :key="app.id"
        class="carousel-dot"
        :class="{ active: index === activeIndex }"
        type="button"
        :aria-label="`Show ${app.name}`"
        @click="setActiveSlide(index)"
      ></button>
    </div>
  </div>
</template>

<style scoped>
.market-carousel {
  position: relative;
  padding: 0 0 28px;
}

.carousel-heading {
  margin-bottom: 10px;
}

.carousel-slide {
  width: 100%;
  height: 420px;
  display: grid;
  grid-template-columns: minmax(0, 1.58fr) minmax(390px, 1fr);
  align-items: stretch;
  padding: 0;
  overflow: hidden;
  border: 0;
  background: #0e141b;
  box-shadow: 0 0 0 1px rgba(0, 0, 0, 0.45), 0 18px 38px rgba(0, 0, 0, 0.5);
  color: inherit;
  cursor: pointer;
  text-align: left;
}

.market-carousel-compact .carousel-slide {
  height: 390px;
}

.carousel-main {
  min-width: 0;
  min-height: 0;
  height: 100%;
  background: #0e141b;
  border-right: 1px solid rgba(255, 255, 255, 0.04);
  overflow: hidden;
}

.carousel-main-media {
  width: 100%;
  height: 100%;
  min-height: 0;
  padding: 10px;
  background: #0e141b;
}

.market-carousel-compact .carousel-main-media {
  height: 100%;
}

.carousel-main-media img,
.carousel-placeholder {
  width: 100%;
  height: 100%;
  object-fit: contain;
  display: block;
}

.carousel-placeholder {
  display: grid;
  place-items: center;
  color: #66c0f4;
  font-size: 4rem;
  font-weight: 800;
}

.carousel-copy {
  min-height: 0;
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.carousel-copy p {
  margin: 0;
  color: #dbe8f4;
  font-size: 0.9rem;
  line-height: 1.45;
  display: -webkit-box;
  -webkit-line-clamp: 3;
  -webkit-box-orient: vertical;
  overflow: hidden;
}

.carousel-stats {
  display: flex;
  gap: 8px;
  color: #9fb0c2;
  font-size: 0.85rem;
}

.carousel-stats span {
  display: inline-flex;
  align-items: center;
  gap: 5px;
}

.carousel-tag {
  color: #c7d5e0;
  background: rgba(103, 193, 245, 0.13);
  padding: 5px 8px;
}

.carousel-screenshots {
  min-width: 0;
  min-height: 0;
  height: 100%;
  padding: 20px 18px 18px;
  display: grid;
  grid-template-rows: auto minmax(0, 1fr) auto;
  gap: 14px;
  background:
    linear-gradient(135deg, rgba(42, 51, 64, 0.98), rgba(16, 27, 39, 0.98)),
    repeating-linear-gradient(45deg, rgba(255, 255, 255, 0.025) 0 1px, transparent 1px 8px);
  overflow: hidden;
}

.carousel-screenshots h3 {
  margin: 0;
  color: #ffffff;
  font-size: 1.55rem;
  font-weight: 500;
  line-height: 1.2;
}

.carousel-creator-link {
  width: fit-content;
  margin-top: -8px;
  padding: 0;
  border: 0;
  background: transparent;
  color: #66c0f4;
  font-size: 0.84rem;
  font-weight: 700;
  cursor: pointer;
}

.carousel-creator-link:hover {
  color: #8fd6ff;
}

.screenshot-grid {
  width: 100%;
  min-height: 0;
  height: 100%;
  display: grid;
  grid-template-columns: 1fr 1fr;
  grid-template-rows: 1fr 1fr;
  gap: 10px;
  align-content: center;
}

.screenshot-tile {
  border: 1px solid rgba(103, 193, 245, 0.12);
  min-height: 0;
  display: grid;
  place-items: center;
  overflow: hidden;
  padding: 0;
  background:
    radial-gradient(circle at center, rgba(103, 193, 245, 0.12), rgba(10, 17, 24, 0.96));
  cursor: pointer;
  transition: border-color 0.16s ease, transform 0.16s ease;
}

.screenshot-tile:hover,
.screenshot-tile:focus-visible {
  border-color: rgba(103, 193, 245, 0.8);
  transform: translateY(-1px);
  outline: 0;
}

.screenshot-tile img {
  width: calc(100% - 10px);
  height: calc(100% - 10px);
  object-fit: contain;
  background: rgba(10, 17, 24, 0.96);
}

.carousel-arrow {
  position: absolute;
  top: 50%;
  z-index: 2;
  width: 54px;
  height: 112px;
  transform: translateY(-50%);
  border: 0;
  background: linear-gradient(90deg, rgba(103, 193, 245, 0.28), rgba(103, 193, 245, 0));
  color: #ffffff;
  cursor: pointer;
  font-size: 2rem;
}

.carousel-arrow-left {
  left: -54px;
}

.carousel-arrow-right {
  right: -54px;
  background: linear-gradient(270deg, rgba(103, 193, 245, 0.28), rgba(103, 193, 245, 0));
}

.carousel-arrow:hover {
  color: #66c0f4;
}

.carousel-dots {
  display: flex;
  justify-content: center;
  gap: 6px;
  margin-top: 12px;
}

.carousel-dot {
  width: 16px;
  height: 9px;
  border: 0;
  border-radius: 999px;
  background: rgba(143, 152, 160, 0.55);
  cursor: pointer;
}

.carousel-dot.active {
  background: #66c0f4;
}

.market-carousel-hero {
  padding: 0;
}

.market-carousel-hero .carousel-heading {
  display: inline-flex;
  margin-bottom: 10px;
  color: #50d7ff;
}

.market-carousel-hero .carousel-slide {
  height: 360px;
  grid-template-columns: minmax(0, 1.12fr) minmax(280px, 0.88fr);
  border: 1px solid rgba(255, 255, 255, 0.12);
  border-radius: 8px;
  background: rgba(15, 18, 24, 0.94);
  box-shadow: 0 24px 80px rgba(0, 0, 0, 0.38);
}

.market-carousel-hero .carousel-main {
  background: #080b10;
}

.market-carousel-hero .carousel-main-media {
  padding: 14px;
  background: #080b10;
}

.market-carousel-hero .carousel-screenshots {
  padding: 18px;
  gap: 12px;
  background:
    linear-gradient(135deg, rgba(36, 48, 64, 0.98), rgba(18, 32, 45, 0.98)),
    repeating-linear-gradient(45deg, rgba(255, 255, 255, 0.025) 0 1px, transparent 1px 8px);
}

.market-carousel-hero .carousel-screenshots h3 {
  font-size: clamp(1.25rem, 2vw, 1.8rem);
  font-weight: 700;
  letter-spacing: -0.03em;
}

.market-carousel-hero .carousel-copy p {
  -webkit-line-clamp: 4;
}

.market-carousel-hero .carousel-tag {
  color: #45e68a;
  background: rgba(69, 230, 138, 0.14);
}

.market-carousel-hero .carousel-arrow {
  width: 42px;
  height: 88px;
  background: rgba(22, 36, 50, 0.78);
  border: 1px solid rgba(255, 255, 255, 0.08);
}

.market-carousel-hero .carousel-arrow-left {
  left: -12px;
}

.market-carousel-hero .carousel-arrow-right {
  right: -12px;
}

@media (max-width: 900px) {
  .carousel-slide,
  .market-carousel-compact .carousel-slide,
  .market-carousel-hero .carousel-slide {
    height: auto;
    grid-template-columns: 1fr;
  }

  .carousel-main,
  .carousel-screenshots {
    height: auto;
  }

  .carousel-screenshots {
    padding: 16px;
  }

  .carousel-arrow-left {
    left: 0;
  }

  .carousel-arrow-right {
    right: 0;
  }
}

@media (max-width: 620px) {
  .screenshot-grid {
    grid-template-columns: 1fr 1fr;
  }

  .carousel-main-media,
  .market-carousel-compact .carousel-main-media {
    height: 190px;
  }
}
</style>
