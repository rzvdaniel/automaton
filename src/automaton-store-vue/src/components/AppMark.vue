<script setup lang="ts">
import { computed } from 'vue'
import { resolveMarketplaceAssetUrl } from '@/composables/useMarketplaceApps'

// Same palette and hash as Studio's AppMark (src/automaton-studio-vue/src/features/apps/utils/app-list-display.ts).
// `dark` lifts the two dark tints, which would vanish on dark cards.
const palette: { background: string; color: string; dark?: string }[] = [
  { background: '#fde3d2', color: '#a23d05' },
  { background: '#17181c', color: '#ff7a2e', dark: '#34353c' },
  { background: '#e6e4dd', color: '#17181c' },
  { background: '#f26a1b', color: '#17181c' },
  { background: '#2a2b31', color: '#f1f0eb', dark: '#42434b' },
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
  const { background, color, dark } = colors
  return { ...box.value, '--mark-bg': background, '--mark-bg-dark': dark ?? background, color, fontSize: `${Math.round(props.size * 0.45)}px` }
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
  background: var(--mark-bg);
}
@media (prefers-color-scheme: dark) {
  .app-mark {
    background: var(--mark-bg-dark);
  }
}
.image {
  object-fit: cover;
  background: var(--panel);
}
</style>
