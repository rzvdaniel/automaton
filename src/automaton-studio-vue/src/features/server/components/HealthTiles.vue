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
  padding: 0.9rem 1rem;
  border: 1px solid var(--p-content-border-color);
  border-left-width: 4px;
  border-radius: 8px;
  background: var(--p-content-background);
  min-width: 0;
}

.health-tile--ok { border-left-color: var(--p-green-500); }
.health-tile--warn { border-left-color: var(--p-orange-500); }
.health-tile--danger { border-left-color: var(--p-red-500); }
.health-tile--unknown { border-left-color: var(--p-surface-400); }

.health-tile__label,
.health-tile__detail {
  color: var(--p-text-muted-color);
  font-size: 0.85rem;
  overflow-wrap: anywhere;
}

.health-tile__value {
  font-size: 1.15rem;
  font-weight: 600;
}
</style>
