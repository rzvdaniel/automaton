<script setup lang="ts">
import { computed } from 'vue'
import { RouterLink } from 'vue-router'
import { resolveMarketplaceAssetUrl } from '@/composables/useMarketplaceApps'
import { publicAppConfig } from '@/config/publicAppConfig'
import type { MarketplaceAppCard } from '@/types/marketplace'

// A cover tile. The app name is a link stretched over the whole card; the creator link sits above it.
const props = defineProps<{ app: MarketplaceAppCard }>()

const creator = computed(() => props.app.creatorDisplayName || props.app.creatorPublicUsername || '')
const installable = computed(() => Boolean(props.app.isForkable && publicAppConfig.studioUrl))
const cover = computed(() => (props.app.thumbnailUrl ? resolveMarketplaceAssetUrl(props.app.thumbnailUrl) : ''))
const summary = computed(() => props.app.summary || props.app.description || 'A public Automaton app')
</script>

<template>
  <article class="tile">
    <img v-if="cover" class="cover" :src="cover" alt="" loading="lazy" />
    <div v-else class="cover placeholder" aria-hidden="true"><span>{{ app.name }}</span></div>
    <div class="body">
      <div class="row">
        <RouterLink class="name" :to="`/apps/${app.id}`">{{ app.name }}</RouterLink>
        <span v-if="app.isFeatured" class="badge signal">Featured</span>
        <span v-if="app.canLaunch" class="badge open">Open to use</span>
        <span v-else-if="installable" class="badge">Installable</span>
      </div>
      <p class="summary">{{ summary }}</p>
      <p v-if="creator || app.category" class="meta">
        <template v-if="creator">
          <RouterLink v-if="app.creatorPublicUsername" class="creator" :to="`/creators/${app.creatorPublicUsername}`">{{ creator }}</RouterLink>
          <template v-else>{{ creator }}</template>
        </template>
        <template v-if="creator && app.category"> · </template>
        <template v-if="app.category">{{ app.category }}</template>
      </p>
    </div>
  </article>
</template>

<style scoped>
.tile {
  position: relative;
  display: grid;
  grid-template-rows: auto 1fr;
  overflow: hidden;
  border-radius: 12px;
  background: var(--card);
  border: 1px solid var(--line);
  transition: border-color 0.15s ease, transform 0.15s ease;
}
.tile:hover,
.tile:focus-within {
  border-color: var(--signal);
  transform: translateY(-2px);
}
@media (prefers-reduced-motion: reduce) {
  .tile {
    transition: border-color 0.15s ease;
  }
  .tile:hover,
  .tile:focus-within {
    transform: none;
  }
}
.cover {
  display: block;
  width: 100%;
  aspect-ratio: 16 / 9;
  object-fit: cover;
  background: var(--panel);
}
.placeholder {
  display: grid;
  place-items: center;
  padding: 16px;
  text-align: center;
  background: color-mix(in srgb, var(--signal) 22%, var(--panel));
  color: var(--signal-ink);
  font: 700 1.3rem/1.15 var(--display);
  overflow-wrap: anywhere;
}
.body {
  display: grid;
  gap: 6px;
  align-content: start;
  padding: 14px 16px 16px;
  min-width: 0;
}
.row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 6px 10px;
}
.site .name {
  font: 600 1.1rem var(--display);
  text-decoration: none;
  color: var(--ink);
  overflow-wrap: anywhere;
}
/* Stretch the name link over the whole tile. */
.site .name::after {
  content: '';
  position: absolute;
  inset: 0;
}
.badge {
  font: 500 0.7rem var(--mono);
  padding: 2px 8px;
  border-radius: 999px;
  border: 1px solid currentColor;
  color: var(--ink-soft);
  white-space: nowrap;
}
.badge.open {
  color: var(--ok, #2f7d4f);
}
.badge.signal {
  background: var(--signal);
  border-color: var(--signal);
  color: var(--on-signal);
}
.summary {
  color: var(--ink-soft);
  font-size: 0.9rem;
  overflow-wrap: anywhere;
  display: -webkit-box;
  -webkit-line-clamp: 3;
  line-clamp: 3;
  -webkit-box-orient: vertical;
  overflow: hidden;
}
.meta {
  font: 0.78rem var(--mono);
  color: var(--ink-soft);
}
.site .creator {
  position: relative;
  text-decoration: none;
}
.site .creator:hover {
  text-decoration: underline;
  text-decoration-color: var(--signal);
}
</style>
