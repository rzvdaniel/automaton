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

const formattedDate = computed(() => formatPublishedDate(props.app.publishedAt))

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

          <!-- Quick Info on Hover -->
          <div class="quick-info">
            <h4 class="quick-title">{{ app.name }}</h4>
            <div class="quick-meta">
              <span v-if="app.category" class="meta-tag">{{ app.category }}</span>
              <span class="meta-views">
                <i class="pi pi-eye"></i>
                {{ app.viewCount }}
              </span>
            </div>
          </div>
        </div>
      </transition>
    </div>

    <!-- Title (Always Visible) -->
    <div class="card-title">
      <h4 class="title-text">{{ app.name }}</h4>
      <p v-if="app.summary" class="title-summary">{{ app.summary }}</p>
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
  left: 8px;
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
  justify-content: flex-start;
  padding-top: 1rem;
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

/* Quick Info */
.quick-info {
  position: absolute;
  bottom: 0;
  left: 0;
  right: 0;
  background: linear-gradient(to top, rgba(0, 0, 0, 0.9), transparent);
  padding: 1.5rem 1rem 1rem;
  color: white;
}

.quick-title {
  margin: 0 0 0.5rem 0;
  font-size: 1rem;
  font-weight: 600;
  color: white;
  line-height: 1.3;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
}

.quick-meta {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  font-size: 0.85rem;
  color: rgba(255, 255, 255, 0.8);
}

.meta-tag {
  background: rgba(255, 255, 255, 0.2);
  padding: 0.25rem 0.5rem;
  border-radius: 4px;
  white-space: nowrap;
}

.meta-views {
  display: flex;
  align-items: center;
  gap: 0.25rem;
}

/* Card Title */
.card-title {
  padding: 0.75rem 0;
  min-height: 4rem;
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.title-text {
  margin: 0;
  font-size: 0.95rem;
  font-weight: 600;
  color: var(--p-text-color);
  display: -webkit-box;
  -webkit-line-clamp: 1;
  -webkit-box-orient: vertical;
  overflow: hidden;
  line-height: 1.3;
}

.title-summary {
  margin: 0;
  font-size: 0.85rem;
  font-weight: 400;
  color: var(--p-text-muted-color);
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
  line-height: 1.4;
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
