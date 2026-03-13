<script setup lang="ts">
import { computed } from 'vue'

const props = defineProps<{
  appId: string
  name: string
}>()

// Curated gradient palette — each pair chosen to look great as an app cover
const gradients = [
  ['#667eea', '#764ba2'], // violet
  ['#4facfe', '#00f2fe'], // ocean blue
  ['#43e97b', '#38f9d7'], // mint
  ['#fa709a', '#fee140'], // sunset
  ['#a18cd1', '#fbc2eb'], // lavender
  ['#fccb90', '#d57eeb'], // peach purple
  ['#a1c4fd', '#c2e9fb'], // sky
  ['#f093fb', '#f5576c'], // pink red
  ['#0ba360', '#3cba92'], // teal
  ['#f7971e', '#ffd200'], // golden
  ['#30cfd0', '#330867'], // cyan deep
  ['#f953c6', '#b91d73'], // magenta
]

// Deterministic index from app id — consistent across renders
const gradient = computed(() => {
  let hash = 0
  for (let i = 0; i < props.appId.length; i++) {
    hash = (hash * 31 + props.appId.charCodeAt(i)) >>> 0
  }
  const [from, to] = gradients[hash % gradients.length]
  return `linear-gradient(135deg, ${from} 0%, ${to} 100%)`
})

// 1–2 uppercase initials
const initials = computed(() => {
  return props.name
    .split(/\s+/)
    .slice(0, 2)
    .map(w => w[0] ?? '')
    .join('')
    .toUpperCase()
})
</script>

<template>
  <div class="cover-placeholder" :style="{ background: gradient }">
    <span class="cover-initials">{{ initials }}</span>
  </div>
</template>

<style scoped>
.cover-placeholder {
  position: absolute;
  inset: 0;
  display: flex;
  align-items: center;
  justify-content: center;
}

.cover-initials {
  font-size: 2.5rem;
  font-weight: 800;
  color: rgba(255, 255, 255, 0.9);
  letter-spacing: 0.05em;
  text-shadow: 0 2px 8px rgba(0, 0, 0, 0.25);
  user-select: none;
}
</style>
