<script setup lang="ts">
import { computed } from 'vue'
import { RouterLink } from 'vue-router'
import { getPublishedAppUrl } from '@/config/publicAppConfig'
import { resolveMarketplaceAssetUrl } from '@/composables/useMarketplaceApps'
import type { MarketplaceAppCard } from '@/types/marketplace'

const props = defineProps<{ app: MarketplaceAppCard }>()

const initial = computed(() => [...props.app.name.trim()][0]?.toUpperCase() ?? '?')
const thumbnail = computed(() => resolveMarketplaceAssetUrl(props.app.thumbnailUrl))
const launchUrl = computed(() => (props.app.canLaunch ? getPublishedAppUrl(props.app.publicUrl) : ''))
const creator = computed(() => props.app.creatorDisplayName || props.app.creatorPublicUsername || '')
const summary = computed(() => props.app.summary || props.app.description || 'A public Automaton app')
</script>

<template>
  <article class="app-row">
    <img v-if="thumbnail" class="app-thumb" :src="thumbnail" alt="" loading="lazy" />
    <span v-else class="app-thumb app-initial" aria-hidden="true">{{ initial }}</span>
    <div class="app-text">
      <div class="app-title">
        <RouterLink class="app-name" :to="`/apps/${app.id}`">{{ app.name }}</RouterLink>
        <span v-if="app.isFeatured" class="badge">Featured</span>
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
    <a v-if="launchUrl" class="btn small" :href="launchUrl" target="_blank" rel="noopener" :aria-label="`Open ${app.name}`">Open</a>
  </article>
</template>

<style scoped>
.app-row {
  display: grid;
  grid-template-columns: 56px minmax(0, 1fr) auto;
  gap: 16px;
  align-items: center;
  padding: 14px;
  border-radius: 18px;
  background: var(--paper);
  border: 1px solid var(--line);
}
.app-thumb {
  width: 56px;
  height: 56px;
  border-radius: 14px;
  object-fit: cover;
  border: 1.5px solid var(--ink);
}
.app-initial {
  display: grid;
  place-items: center;
  background: var(--signal);
  color: var(--on-signal);
  font: 800 1.5rem var(--display);
}
.app-text {
  display: grid;
  gap: 2px;
  min-width: 0;
}
.app-title {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}
.site .app-name {
  font: 700 1.15rem var(--display);
  text-decoration: none;
}
.site .app-name:hover {
  text-decoration: underline;
  text-decoration-color: var(--signal);
}
.badge {
  font: 600 0.7rem var(--body);
  letter-spacing: 0.08em;
  text-transform: uppercase;
  padding: 3px 8px;
  border-radius: 999px;
  background: var(--signal);
  color: var(--on-signal);
}
.app-summary,
.app-by {
  color: var(--ink-soft);
  font-size: 0.92rem;
  overflow-wrap: anywhere;
}
.app-by {
  font-size: 0.82rem;
}

@media (max-width: 620px) {
  .app-row {
    grid-template-columns: 48px minmax(0, 1fr);
  }
  .app-row .btn {
    grid-column: 2;
    justify-self: start;
  }
  .app-thumb {
    width: 48px;
    height: 48px;
  }
}
</style>
