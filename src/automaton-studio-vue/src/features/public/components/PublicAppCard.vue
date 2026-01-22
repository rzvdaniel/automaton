<script setup lang="ts">
import { computed, ref } from 'vue'
import Button from 'primevue/button'
import Tag from 'primevue/tag'
import type { PublicAppCard as AppCardType } from '../types/publicApps.types'
import { formatPublishedDate } from '../types/publicApps.types'

const props = defineProps<{
  app: AppCardType
}>()

const emit = defineEmits<{
  (e: 'open', app: AppCardType): void
  (e: 'details', app: AppCardType): void
}>()

const isHovering = ref(false)
const summaryExpanded = ref(false)

const formattedDate = computed(() => formatPublishedDate(props.app.publishedAt))

// Check if summary is long enough to need expansion
const isSummaryLong = computed(() => {
  return (props.app.summary?.length ?? 0) > 80
})

// Full thumbnail URL with API base URL
const fullThumbnailUrl = computed(() => {
  if (!props.app.thumbnailUrl) return ''
  if (props.app.thumbnailUrl.startsWith('http')) {
    return props.app.thumbnailUrl
  }
  const baseUrl = import.meta.env.VITE_API_BASE_URL || ''
  return `${baseUrl}${props.app.thumbnailUrl}`
})

function openApp() {
  emit('open', props.app)
}

function showDetails() {
  emit('details', props.app)
}

function toggleSummary(e: Event) {
  e.stopPropagation()
  summaryExpanded.value = !summaryExpanded.value
}
</script>

<template>
  <div class="netflix-card" @mouseenter="isHovering = true" @mouseleave="isHovering = false" @click="showDetails">
    <!-- Thumbnail Container -->
    <div class="thumbnail-container">
      <img
        v-if="app.thumbnailUrl"
        :src="fullThumbnailUrl"
        :alt="app.name"
        class="thumbnail-image"
      />
      <div v-else class="thumbnail-placeholder">
        <i class="pi pi-box"></i>
      </div>

      <!-- Featured Badge -->
      <Tag v-if="app.isFeatured" value="Featured" severity="warning" class="featured-badge" />

      <!-- Views Pill (always visible, including on hover) -->
      <div v-if="app.viewCount > 0" class="views-pill">
        <i class="pi pi-eye"></i>
        <span>{{ app.viewCount }}</span>
      </div>

      <!-- Hover Overlay -->
      <transition name="fade">
        <div v-if="isHovering" class="hover-overlay">
          <div class="overlay-content">
            <Button
              icon="pi pi-play"
              rounded
              class="play-button"
              @click.stop="openApp"
              v-tooltip.top="'Open app'"
            />
            <Button
              icon="pi pi-info-circle"
              rounded
              text
              class="info-button"
              @click.stop="showDetails"
              v-tooltip.top="'View details'"
            />
          </div>
        </div>
      </transition>
    </div>

    <!-- Combined Title + Summary as one paragraph -->
    <div class="card-text">
      <p :class="['text-line', { 'expanded': summaryExpanded }]">
        <span class="text-title">{{ app.name }}</span>
        <span v-if="app.summary" class="text-summary">{{ app.summary }}</span>
      </p>
      <button 
        v-if="isSummaryLong"
        class="summary-toggle"
        @click="toggleSummary"
      >
        {{ summaryExpanded ? 'Show less' : 'Show more' }}
        <i :class="summaryExpanded ? 'pi pi-chevron-up' : 'pi pi-chevron-down'"></i>
      </button>
    </div>
  </div>
</template>

<style scoped>
.netflix-card {
  cursor: pointer;
  display: flex;
  flex-direction: column;
}

/* Thumbnail Container */
.thumbnail-container {
  position: relative;
  width: 100%;
  padding-bottom: 56.25%; /* 16:9 aspect ratio */
  overflow: hidden;
  border-radius: var(--p-border-radius);
  background: var(--p-surface-200);
}

:global(.app-dark) .thumbnail-container {
  background: var(--p-surface-700);
}

.thumbnail-image {
  position: absolute;
  top: 0;
  left: 0;
  width: 100%;
  height: 100%;
  object-fit: cover;
  transition: transform 0.3s ease;
}

.netflix-card:hover .thumbnail-image {
  transform: scale(1.05);
}

.thumbnail-placeholder {
  position: absolute;
  top: 0;
  left: 0;
  width: 100%;
  height: 100%;
  display: flex;
  align-items: center;
  justify-content: center;
  background: linear-gradient(135deg, var(--p-primary-100) 0%, var(--p-primary-200) 100%);
}

:global(.app-dark) .thumbnail-placeholder {
  background: linear-gradient(135deg, var(--p-surface-700) 0%, var(--p-surface-800) 100%);
}

.thumbnail-placeholder i {
  font-size: 2.5rem;
  color: var(--p-primary-500);
  opacity: 0.6;
}

/* Featured Badge */
.featured-badge {
  position: absolute;
  top: 8px;
  right: 8px;
  z-index: 10;
}

/* Hover Overlay */
.hover-overlay {
  position: absolute;
  top: 0;
  left: 0;
  width: 100%;
  height: 100%;
  background: rgba(0, 0, 0, 0.7);
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding-top: 0;
  z-index: 20;
}

.overlay-content {
  display: flex;
  gap: 1rem;
  margin-bottom: 1rem;
}

.play-button {
  background: var(--p-primary-500) !important;
  width: 3rem;
  height: 3rem;
  border-radius: 50%;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 1.2rem;
  transition: all 0.2s ease;
}

.play-button:hover {
  background: var(--p-primary-600) !important;
  transform: scale(1.1);
}

.info-button {
  width: 3rem;
  height: 3rem;
  border-radius: 50%;
  border: 2px solid rgba(255, 255, 255, 0.7);
  color: white;
  font-size: 1.2rem;
  transition: all 0.2s ease;
}

.info-button:hover {
  border-color: white;
  background: rgba(255, 255, 255, 0.1) !important;
  transform: scale(1.1);
}

/* Combined Title + Summary block */
.card-text {
  padding: 0.5rem 0 0.5rem 0;
}

.text-line {
  margin: 0;
  color: var(--p-text-color);
  line-height: 1.45;
  display: -webkit-box;
  -webkit-line-clamp: 3; /* allow wrapping across lines */
  -webkit-box-orient: vertical;
  overflow: hidden;
  transition: all 0.3s ease;
}

.text-line.expanded {
  -webkit-line-clamp: unset;
  display: block;
}

.text-title {
  font-weight: 700;
  font-size: 1rem;
  margin-right: 0.25rem;
}

.text-summary {
  color: var(--p-text-muted-color);
  font-size: 0.92rem;
}

.summary-toggle {
  margin-top: 0.25rem;
  padding: 0;
  background: none;
  border: none;
  color: var(--p-primary-500);
  font-size: 0.8rem;
  font-weight: 500;
  cursor: pointer;
  display: flex;
  align-items: center;
  gap: 0.25rem;
  transition: color 0.2s ease;
}

.summary-toggle:hover {
  color: var(--p-primary-600);
  text-decoration: underline;
}

.summary-toggle i {
  font-size: 0.7rem;
}

/* Views pill */
.views-pill {
  position: absolute;
  right: 8px;
  bottom: 8px;
  display: flex;
  align-items: center;
  gap: 0.25rem;
  padding: 0.25rem 0.5rem;
  background: rgba(0, 0, 0, 0.6);
  color: #fff;
  border-radius: 999px;
  font-size: 0.8rem;
  line-height: 1;
  z-index: 30; /* above hover overlay */
  pointer-events: none;
}

.views-pill i {
  font-size: 0.75rem;
}

/* Fade transition */
.fade-enter-active,
.fade-leave-active {
  transition: opacity 0.2s ease;
}

.fade-enter-from,
.fade-leave-to {
  opacity: 0;
}
</style>
