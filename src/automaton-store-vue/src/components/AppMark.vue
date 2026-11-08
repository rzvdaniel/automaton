<script setup lang="ts">
import { computed } from 'vue'
import { resolveMarketplaceAssetUrl } from '@/composables/useMarketplaceApps'

// The generated mark Studio shows for an app without an image (same palette and hash).
const palette = [
  { background: '#fde3d2', color: '#a23d05' },
  { background: '#17181c', color: '#ff7a2e' },
  { background: '#e6e4dd', color: '#17181c' },
  { background: '#f26a1b', color: '#17181c' },
  { background: '#2a2b31', color: '#f1f0eb' },
]

const props = withDefaults(defineProps<{ id: string; name: string; imageUrl?: string; size?: number }>(), {
  imageUrl: '',
  size: 40,
})

const image = computed(() => (props.imageUrl ? resolveMarketplaceAssetUrl(props.imageUrl) : ''))
const initial = computed(() => (Array.from(props.name.trim())[0] ?? '?').toUpperCase())
const box = computed(() => ({
  width: `${props.size}px`,
  height: `${props.size}px`,
  borderRadius: `${Math.round(props.size * 0.275)}px`,
}))
const generated = computed(() => {
  let hash = 0x811c9dc5
  for (let index = 0; index < props.id.length; index++) {
    hash ^= props.id.charCodeAt(index)
    hash = Math.imul(hash, 0x01000193) >>> 0
  }
  const colors = palette[hash % palette.length]!
  return { ...box.value, ...colors, fontSize: `${Math.round(props.size * 0.45)}px` }
})
</script>

<template>
  <img v-if="image" class="app-mark image" :style="box" :src="image" alt="" loading="lazy" />
  <span v-else class="app-mark" :style="generated" aria-hidden="true">{{ initial }}</span>
</template>

<style scoped>
.app-mark {
  flex: none;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  font-family: var(--display);
  font-weight: 700;
  line-height: 1;
}
.image {
  object-fit: cover;
  background: var(--panel);
}
</style>
