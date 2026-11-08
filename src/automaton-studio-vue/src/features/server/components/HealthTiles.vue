<script setup lang="ts">
import { computed } from 'vue'
import type { ServerHealth } from '../types/server.types'
import { healthTiles } from '../utils/health-tiles'

const props = defineProps<{ health: ServerHealth }>()
const tiles = computed(() => healthTiles(props.health))
</script>

<template>
  <div class="health-tiles" data-testid="health-tiles">
    <div v-for="tile in tiles" :key="tile.key" class="health-tile" :class="`health-tile--${tile.level}`" :data-testid="`tile-${tile.key}`">
      <span class="health-tile__label">{{ tile.label }}</span>
      <span class="health-tile__value">{{ tile.value }}</span>
      <span class="health-tile__detail">{{ tile.detail }}</span>
    </div>
  </div>
</template>

<style scoped>
.health-tiles {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(10.5rem, 1fr));
  gap: 0.75rem;
}

.health-tile {
  display: flex;
  flex-direction: column;
  gap: 0.2rem;
  padding: 14px 16px;
  border: 1px solid var(--line);
  border-radius: 18px;
  background: var(--paper);
  min-width: 0;
}

.health-tile__label::before { content: ''; display: inline-block; width: 8px; height: 8px; margin-right: 6px; border-radius: 50%; background: var(--ink-soft); }
.health-tile--ok .health-tile__label::before { background: var(--ok); }
.health-tile--warn .health-tile__label::before,
.health-tile--danger .health-tile__label::before { background: var(--signal); }

.health-tile__label,
.health-tile__detail {
  color: var(--ink-soft);
  font-size: 13px;
  overflow-wrap: anywhere;
}

.health-tile__value {
  font-size: 1.15rem;
  font-weight: 600;
}
</style>
