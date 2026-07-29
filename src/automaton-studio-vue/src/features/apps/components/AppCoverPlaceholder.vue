<script setup lang="ts">
import { computed } from 'vue'

const props = defineProps<{
  appId: string
  name: string
}>()

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
  <div class="cover-placeholder">
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
  background: var(--p-surface-100);
}

.cover-initials {
  font-size: 2.5rem;
  font-weight: 800;
  color: var(--p-primary-600);
  letter-spacing: 0.05em;
  user-select: none;
}
</style>
