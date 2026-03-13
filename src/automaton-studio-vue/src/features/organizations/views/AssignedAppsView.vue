<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import Button from 'primevue/button'
import Tag from 'primevue/tag'
import EmptyState from '@/shared/components/EmptyState.vue'
import appsService from '@/features/apps/services/apps.service'
import type { App } from '@/features/apps/types/apps.types'

const apps = ref<App[]>([])
const loading = ref(true)

onMounted(async () => {
  await loadApps()
})

async function loadApps() {
  try {
    loading.value = true
    apps.value = await appsService.list()
  } catch {
    // handled by axios interceptor
  } finally {
    loading.value = false
  }
}

// Hover state per card
const hovering = ref<Record<string, boolean>>({})

function setHovering(id: string, value: boolean) {
  hovering.value[id] = value
}

// Picsum deterministic fallback
function thumbnailSrc(app: App): string {
  if (!app.thumbnailUrl) return `https://picsum.photos/seed/${app.id}/500/300`
  if (app.thumbnailUrl.startsWith('http')) return app.thumbnailUrl
  const base = import.meta.env.VITE_API_BASE_URL || ''
  return `${base}${app.thumbnailUrl}`
}

function openApp(app: App) {
  if (app.publicUrl) {
    window.open(app.publicUrl, '_blank')
  }
}
</script>

<template>
  <div class="my-apps-page">
    <div class="page-header">
      <h1 class="page-title">My Apps</h1>
      <p class="page-subtitle">Apps available to you</p>
    </div>

    <div v-if="loading" class="loading-state">
      <i class="pi pi-spin pi-spinner" style="font-size: 2rem; color: var(--p-primary-500)"></i>
      <p>Loading your apps…</p>
    </div>

    <EmptyState
      v-else-if="apps.length === 0"
      icon="pi-inbox"
      title="No apps yet"
      message="Apps assigned to you will appear here"
    />

    <div v-else class="apps-grid">
      <div
        v-for="app in apps"
        :key="app.id"
        class="app-card"
        @mouseenter="setHovering(app.id, true)"
        @mouseleave="setHovering(app.id, false)"
        @click="openApp(app)"
      >
        <!-- Image -->
        <div class="card-image">
          <img :src="thumbnailSrc(app)" :alt="app.name" loading="lazy" />

          <!-- Published badge -->
          <Tag
            v-if="app.isPublished"
            value="Live"
            severity="success"
            class="live-badge"
          />

          <!-- Hover overlay -->
          <transition name="fade">
            <div v-if="hovering[app.id]" class="hover-overlay">
              <Button
                v-tooltip.top="'Open app'"
                icon="pi pi-play"
                rounded
                class="play-btn"
                @click.stop="openApp(app)"
              />
            </div>
          </transition>
        </div>

        <!-- Text -->
        <div class="card-body">
          <p class="card-title">{{ app.name }}</p>
          <p v-if="app.description || app.summary" class="card-desc">
            {{ app.description || app.summary }}
          </p>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.my-apps-page {
  padding: 2rem;
}

.page-header {
  margin-bottom: 2rem;
}

.page-title {
  margin: 0 0 0.25rem;
  font-size: 1.75rem;
  font-weight: 700;
}

.page-subtitle {
  margin: 0;
  color: var(--p-text-muted-color);
  font-size: 0.95rem;
}

.loading-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 1rem;
  padding: 4rem 0;
  color: var(--p-text-muted-color);
}

/* Netflix-style grid */
.apps-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(240px, 1fr));
  gap: 1.5rem;
}

/* Card */
.app-card {
  cursor: pointer;
  display: flex;
  flex-direction: column;
  border-radius: 10px;
  overflow: hidden;
  transition: transform 0.2s ease;
}

.app-card:hover {
  transform: translateY(-4px);
}

/* Image area — 16:9 ratio */
.card-image {
  position: relative;
  width: 100%;
  padding-bottom: 56.25%;
  overflow: hidden;
  background: var(--p-surface-200);
}

:global(.app-dark) .card-image {
  background: var(--p-surface-700);
}

.card-image img {
  position: absolute;
  inset: 0;
  width: 100%;
  height: 100%;
  object-fit: cover;
  transition: transform 0.3s ease;
}

.app-card:hover .card-image img {
  transform: scale(1.05);
}

/* Live badge */
.live-badge {
  position: absolute;
  top: 8px;
  left: 8px;
  z-index: 10;
  font-size: 0.72rem;
}

/* Hover overlay */
.hover-overlay {
  position: absolute;
  inset: 0;
  background: rgba(0, 0, 0, 0.6);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 20;
}

.play-btn {
  width: 3rem !important;
  height: 3rem !important;
  background: var(--p-primary-500) !important;
  border-radius: 50%;
  font-size: 1.2rem;
  transition: all 0.2s ease;
}

.play-btn:hover {
  background: var(--p-primary-600) !important;
  transform: scale(1.1);
}

/* Card text */
.card-body {
  padding: 0.6rem 0 0.25rem;
}

.card-title {
  margin: 0 0 0.2rem;
  font-weight: 700;
  font-size: 0.95rem;
  color: var(--p-text-color);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.card-desc {
  margin: 0;
  font-size: 0.85rem;
  color: var(--p-text-muted-color);
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
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

