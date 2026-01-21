<script setup lang="ts">
import { computed } from 'vue'
import Card from 'primevue/card'
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

const displayName = computed(() => props.app.displayName || props.app.name)
const formattedDate = computed(() => formatPublishedDate(props.app.publishedAt))

function openApp() {
  emit('open', props.app)
}

function showDetails() {
  emit('details', props.app)
}
</script>

<template>
  <Card class="app-card" @click="showDetails">
    <!-- Thumbnail -->
    <template #header>
      <div class="card-thumbnail">
        <img
          v-if="app.thumbnailUrl"
          :src="app.thumbnailUrl"
          :alt="displayName"
          class="thumbnail-image"
        />
        <div v-else class="thumbnail-placeholder">
          <i class="pi pi-box"></i>
        </div>
        <Tag v-if="app.isFeatured" value="Featured" severity="warning" class="featured-badge" />
      </div>
    </template>

    <template #content>
      <!-- App Info -->
      <div class="app-info">
        <h3 class="app-name">{{ displayName }}</h3>
        <p v-if="app.description" class="app-description">{{ app.description }}</p>
        <p v-else class="app-description app-description-empty">No description</p>

        <!-- Meta -->
        <div class="app-meta">
          <span v-if="app.category" class="meta-item">
            <i class="pi pi-tag"></i>
            {{ app.category }}
          </span>
          <span v-if="app.viewCount > 0" class="meta-item">
            <i class="pi pi-eye"></i>
            {{ app.viewCount }} views
          </span>
          <span v-if="formattedDate" class="meta-item">
            <i class="pi pi-calendar"></i>
            {{ formattedDate }}
          </span>
        </div>
      </div>

      <!-- Actions -->
      <div class="app-actions">
        <Button
          label="Open App"
          icon="pi pi-external-link"
          size="small"
          @click.stop="openApp"
        />
        <Button
          icon="pi pi-info-circle"
          size="small"
          text
          rounded
          v-tooltip.top="'View details'"
          @click.stop="showDetails"
        />
      </div>
    </template>
  </Card>
</template>

<style scoped>
.app-card {
  cursor: pointer;
  transition: transform 0.2s ease, box-shadow 0.2s ease;
  height: 100%;
  display: flex;
  flex-direction: column;
}

.app-card:hover {
  transform: translateY(-4px);
  box-shadow: 0 8px 24px rgba(0, 0, 0, 0.12);
}

:global(.app-dark) .app-card:hover {
  box-shadow: 0 8px 24px rgba(0, 0, 0, 0.4);
}

.card-thumbnail {
  position: relative;
  width: 100%;
  height: 160px;
  overflow: hidden;
  border-radius: var(--p-border-radius) var(--p-border-radius) 0 0;
}

.thumbnail-image {
  width: 100%;
  height: 100%;
  object-fit: cover;
}

.thumbnail-placeholder {
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
  font-size: 3rem;
  color: var(--p-primary-400);
}

:global(.app-dark) .thumbnail-placeholder i {
  color: var(--p-primary-300);
}

.featured-badge {
  position: absolute;
  top: 0.75rem;
  right: 0.75rem;
}

.app-info {
  flex: 1;
  margin-bottom: 1rem;
}

.app-name {
  margin: 0 0 0.5rem 0;
  font-size: 1.1rem;
  font-weight: 600;
  color: var(--p-text-color);
  line-height: 1.3;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
}

.app-description {
  margin: 0 0 0.75rem 0;
  color: var(--p-text-muted-color);
  font-size: 0.9rem;
  line-height: 1.4;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
}

.app-description-empty {
  font-style: italic;
  opacity: 0.6;
}

.app-meta {
  display: flex;
  flex-wrap: wrap;
  gap: 0.75rem;
}

.meta-item {
  display: flex;
  align-items: center;
  gap: 0.25rem;
  font-size: 0.8rem;
  color: var(--p-text-muted-color);
}

.meta-item i {
  font-size: 0.75rem;
}

.app-actions {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding-top: 0.75rem;
  border-top: 1px solid var(--p-surface-200);
}

:global(.app-dark) .app-actions {
  border-color: var(--p-surface-700);
}

.app-actions :deep(.p-button:first-child) {
  flex: 1;
}
</style>
