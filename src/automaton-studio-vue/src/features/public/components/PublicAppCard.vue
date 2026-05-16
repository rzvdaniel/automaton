<script setup lang="ts">
import { computed } from 'vue'
import Tag from 'primevue/tag'
import type { PublicAppCard as AppCardType } from '../types/publicApps.types'
import AppCoverPlaceholder from '@/features/apps/components/AppCoverPlaceholder.vue'

const props = defineProps<{
  app: AppCardType
}>()

const emit = defineEmits<{
  (e: 'details', app: AppCardType): void
  (e: 'toggle-star', app: AppCardType): void
}>()

const fullThumbnailUrl = computed(() => {
  if (!props.app.thumbnailUrl) return ''
  if (props.app.thumbnailUrl.startsWith('http')) {
    return props.app.thumbnailUrl
  }
  const baseUrl = import.meta.env.VITE_API_BASE_URL || ''
  return `${baseUrl}${props.app.thumbnailUrl}`
})

function showDetails() {
  emit('details', props.app)
}

function toggleStar(event: Event) {
  event.stopPropagation()
  emit('toggle-star', props.app)
}
</script>

<template>
  <article class="store-card" @click="showDetails">
    <div class="cover-frame">
      <img v-if="app.thumbnailUrl" :src="fullThumbnailUrl" :alt="app.name" class="cover-image" />
      <AppCoverPlaceholder v-else :app-id="app.id" :name="app.name" />

      <button class="stars-pill" @click="toggleStar">
        <i :class="app.isStarredByCurrentUser ? 'pi pi-star-fill' : 'pi pi-star'"></i>
        <span>{{ app.starCount }}</span>
      </button>
    </div>

    <div class="card-body">
      <div class="title-row">
        <h3>{{ app.name }}</h3>
        <Tag v-if="app.isFeatured" value="Featured" severity="warning" class="featured-badge" />
      </div>

      <p v-if="app.summary" class="summary">{{ app.summary }}</p>

      <div class="meta-row">
        <span v-if="app.category" class="category-pill">{{ app.category }}</span>
        <span class="meta-stat"><i class="pi pi-eye"></i>{{ app.viewCount }}</span>
      </div>
    </div>
  </article>
</template>

<style scoped>
.store-card {
  cursor: pointer;
  display: flex;
  min-height: 100%;
  background: rgba(22, 34, 49, 0.88);
  border: 1px solid rgba(103, 193, 245, 0.14);
  box-shadow: 0 10px 28px rgba(0, 0, 0, 0.22);
  color: #dbe8f4;
  transition: transform 0.18s ease, border-color 0.18s ease, background 0.18s ease;
}

.store-card:hover {
  transform: translateY(-3px);
  border-color: rgba(103, 193, 245, 0.42);
  background: rgba(32, 50, 70, 0.96);
}

.cover-frame {
  position: relative;
  flex: 0 0 42%;
  min-height: 148px;
  overflow: hidden;
  padding: 0.5rem;
  background: #0e141b;
}

.cover-image {
  position: absolute;
  inset: 0.5rem;
  width: calc(100% - 1rem);
  height: calc(100% - 1rem);
  object-fit: contain;
  transition: filter 0.22s ease;
}

.store-card:hover .cover-image {
  filter: saturate(1.08);
}

.card-body {
  min-width: 0;
  padding: 0.85rem 0.95rem;
  display: flex;
  flex: 1;
  flex-direction: column;
  gap: 0.65rem;
}

.title-row {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 0.75rem;
}

.title-row h3 {
  margin: 0;
  color: #ffffff;
  font-size: 1rem;
  font-weight: 600;
  line-height: 1.25;
}

.featured-badge {
  flex-shrink: 0;
}

.summary {
  margin: 0;
  color: #acb9c7;
  font-size: 0.88rem;
  line-height: 1.4;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
}

.meta-row {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 0.45rem;
  margin-top: auto;
}

.category-pill,
.meta-stat,
.stars-pill {
  display: flex;
  align-items: center;
  gap: 0.35rem;
  font-size: 0.78rem;
  line-height: 1;
}

.category-pill,
.meta-stat {
  color: #c7d5e0;
  background: rgba(103, 193, 245, 0.11);
  padding: 0.35rem 0.55rem;
}

.stars-pill {
  position: absolute;
  left: 0.6rem;
  bottom: 0.6rem;
  z-index: 2;
  padding: 0.38rem 0.58rem;
  border: 1px solid rgba(255, 255, 255, 0.18);
  background: rgba(0, 0, 0, 0.68);
  color: #ffffff;
  cursor: pointer;
}

.stars-pill i {
  font-size: 0.75rem;
}

@media (max-width: 620px) {
  .store-card {
    flex-direction: column;
  }

  .cover-frame {
    flex-basis: auto;
    aspect-ratio: 16 / 9;
    min-height: auto;
  }
}
</style>
