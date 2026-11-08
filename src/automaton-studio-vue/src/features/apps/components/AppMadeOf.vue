<script setup lang="ts">
import { computed } from 'vue'
import type { App } from '../types/apps.types'
import { getMadeOf, type MadeOfItem } from '../utils/app-made-of'

const props = defineProps<{ app: App }>()
const emit = defineEmits<{ select: [key: 'settings' | 'advanced'] }>()

const items = computed(() => getMadeOf(props.app))
const target: Record<MadeOfItem['key'], 'settings' | 'advanced'> = {
  runs: 'advanced',
  source: 'advanced',
  data: 'advanced',
  secrets: 'settings',
  backup: 'advanced',
}
</script>

<template>
  <section class="made-of" aria-labelledby="made-of-title">
    <h2 id="made-of-title">What this app is made of</h2>
    <ul class="made-of-strip">
      <li v-for="item in items" :key="item.key" :data-testid="`made-of-${item.key}`">
        <button type="button" @click="emit('select', target[item.key])">
          <span class="made-of-label">{{ item.label }}</span>
          <span class="made-of-value">{{ item.value }}</span>
          <span class="made-of-detail">{{ item.detail }}</span>
        </button>
      </li>
    </ul>
  </section>
</template>

<style scoped>
.made-of h2 {
  margin: 0 0 0.6rem;
  font-size: 1rem;
}

.made-of-strip {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(10.5rem, 1fr));
  gap: 0.75rem;
  margin: 0;
  padding: 0;
  list-style: none;
}

.made-of-strip button {
  display: flex;
  flex-direction: column;
  gap: 0.2rem;
  width: 100%;
  height: 100%;
  padding: 0.8rem 0.9rem;
  border: 1px solid var(--p-content-border-color);
  border-radius: 0.75rem;
  background: var(--p-content-background);
  color: var(--p-text-color);
  text-align: left;
  cursor: pointer;
}

.made-of-strip button:hover {
  background: var(--p-content-hover-background);
}

.made-of-label {
  color: var(--p-text-muted-color);
  font-size: 0.72rem;
  font-weight: 700;
  letter-spacing: 0.04em;
  text-transform: uppercase;
}

.made-of-value {
  font-size: 1rem;
  font-weight: 600;
}

.made-of-detail {
  color: var(--p-text-muted-color);
  font-size: 0.8rem;
  overflow-wrap: anywhere;
}
</style>
