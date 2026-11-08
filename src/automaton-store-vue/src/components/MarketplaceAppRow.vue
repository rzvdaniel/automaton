<script setup lang="ts">
import { computed } from 'vue'
import { RouterLink } from 'vue-router'
import AppMark from '@/components/AppMark.vue'
import { publicAppConfig } from '@/config/publicAppConfig'
import type { MarketplaceAppCard } from '@/types/marketplace'

const props = defineProps<{ app: MarketplaceAppCard }>()

const creator = computed(() => props.app.creatorDisplayName || props.app.creatorPublicUsername || '')
const installUrl = computed(() =>
  props.app.isForkable && publicAppConfig.studioUrl ? `${publicAppConfig.studioUrl}apps/install/${props.app.id}` : '',
)
const openToUse = computed(() => props.app.canLaunch)
const summary = computed(() => props.app.summary || props.app.description || 'A public Automaton app')
</script>

<template>
  <article class="app-row">
    <AppMark :id="app.id" :name="app.name" :image-url="app.thumbnailUrl" :size="40" />
    <div class="app-text">
      <div class="app-title">
        <RouterLink class="app-name" :to="`/apps/${app.id}`">{{ app.name }}</RouterLink>
        <span v-if="app.isFeatured" class="badge">Featured</span>
        <span v-if="installUrl" class="badge badge-quiet">Installable</span>
        <span v-if="openToUse" class="badge badge-quiet">Open to use</span>
      </div>
      <p class="app-summary">{{ summary }}</p>
      <p v-if="creator || app.category" class="app-by">
        <template v-if="creator">
          by
          <RouterLink v-if="app.creatorPublicUsername" :to="`/creators/${app.creatorPublicUsername}`">{{ creator }}</RouterLink>
          <template v-else>{{ creator }}</template>
        </template>
        <template v-if="creator && app.category"> · </template>
        <template v-if="app.category">{{ app.category }}</template>
      </p>
    </div>
    <div class="app-actions">
      <RouterLink class="btn small" :to="`/apps/${app.id}`" :aria-label="`View ${app.name} details`">Open</RouterLink>
      <a v-if="installUrl" class="btn small" :href="installUrl" :aria-label="`Install ${app.name}`">Install</a>
    </div>
  </article>
</template>

<style scoped>
.app-row {
  display: grid;
  grid-template-columns: 40px minmax(0, 1fr) auto;
  gap: 12px;
  align-items: center;
  padding: 12px;
  border-radius: 16px;
  background: var(--card);
  border: 1px solid var(--line);
}
.app-text {
  display: grid;
  gap: 1px;
  min-width: 0;
}
.app-title {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 6px;
}
.site .app-name {
  font: 700 1rem var(--display);
  text-decoration: none;
}
.site .app-name:hover {
  text-decoration: underline;
  text-decoration-color: var(--signal);
}
.badge {
  font: 600 0.65rem var(--body);
  letter-spacing: 0.08em;
  text-transform: uppercase;
  padding: 2px 7px;
  border-radius: 999px;
  background: var(--signal);
  color: var(--on-signal);
}
.badge-quiet {
  background: transparent;
  color: var(--ink-soft);
  border: 1px solid var(--line);
}
.app-actions {
  display: flex;
  gap: 6px;
}
.app-summary,
.app-by {
  color: var(--ink-soft);
  font-size: 0.85rem;
  overflow-wrap: anywhere;
}
.app-summary {
  display: -webkit-box;
  -webkit-line-clamp: 2;
  line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
}
.app-by {
  font-size: 0.78rem;
}

@media (max-width: 620px) {
  .app-row {
    grid-template-columns: 40px minmax(0, 1fr);
  }
  .app-row .app-actions {
    grid-column: 2;
    justify-self: start;
  }
}
</style>
