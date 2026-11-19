<script setup lang="ts">
import type { CatalogPublisher } from '../types/apps.types'

/** "by <publisher>" with its small logo; Automaton's own apps in the official catalog also carry a verified tick. */
defineProps<{ publisher: CatalogPublisher }>()
</script>

<template>
  <span class="publisher" data-testid="publisher">
    <img v-if="publisher.logo" class="publisher__logo" :src="publisher.logo" alt="" width="14" height="14" loading="lazy" referrerpolicy="no-referrer" />
    <span>by {{ publisher.name }}</span>
    <i v-if="publisher.verified" class="pi pi-verified publisher__tick" role="img" aria-label="Verified publisher" title="Verified: published by Automaton in the official catalog" data-testid="publisher-verified" />
  </span>
</template>

<style scoped>
.publisher { display: inline-flex; align-items: center; gap: 4px; font-size: 0.8125rem; color: var(--ink-muted, inherit); opacity: 0.85; }
.publisher__logo { width: 14px; height: 14px; border-radius: 3px; object-fit: contain; }
.publisher__tick { font-size: 0.8125rem; color: var(--primary-color, #2563eb); }
</style>
