<script setup lang="ts">
import { computed } from 'vue'
import { RouterLink } from 'vue-router'
import { resolveMarketplaceAssetUrl } from '@/composables/useMarketplaceApps'
import { publicAppConfig } from '@/config/publicAppConfig'
import type { MarketplaceAppCard } from '@/types/marketplace'

const props = defineProps<{ app: MarketplaceAppCard }>()

const initial = computed(() => [...props.app.name.trim()][0]?.toUpperCase() ?? '?')
const thumbnail = computed(() => resolveMarketplaceAssetUrl(props.app.thumbnailUrl))
const creator = computed(() => props.app.creatorDisplayName || props.app.creatorPublicUsername || '')
const installUrl = computed(() =>
  props.app.isForkable && publicAppConfig.studioUrl ? `${publicAppConfig.studioUrl}apps/install/${props.app.id}` : '',
)
// An Actions-only app has no web page, so the only useful action is Install.
const showOpen = computed(() => props.app.hasWebPage !== false || !installUrl.value)
const openToUse = computed(() => props.app.hasWebPage !== false && props.app.canLaunch)
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
      <RouterLink v-if="showOpen" class="btn small" :to="`/apps/${app.id}`" :aria-label="`View ${app.name} details`">Open</RouterLink>
      <a v-if="installUrl" class="btn small" :class="{ primary: !showOpen }" :href="installUrl" :aria-label="`Install ${app.name}`">Install</a>
    </div>
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
.badge-quiet {
  background: transparent;
  color: var(--ink-soft);
  border: 1px solid var(--line);
}
.app-actions {
  display: flex;
  gap: 8px;
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
  .app-row .app-actions {
    grid-column: 2;
    justify-self: start;
  }
  .app-thumb {
    width: 48px;
    height: 48px;
  }
}
</style>
