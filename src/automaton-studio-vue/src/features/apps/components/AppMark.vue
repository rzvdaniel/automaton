<script setup lang="ts">
import { computed } from 'vue'
import { resolveMediaUrl } from '../utils/media-url'
import { markColors, markInitial } from '../utils/app-list-display'

const props = withDefaults(defineProps<{
  id: string
  name: string
  imageUrl?: string | null
  size?: number
}>(), { imageUrl: null, size: 40 })

const image = computed(() => resolveMediaUrl(props.imageUrl))
const colors = computed(() => markColors(props.id))
const box = computed(() => ({ width: `${props.size}px`, height: `${props.size}px`, borderRadius: `${Math.round(props.size * 0.275)}px` }))
const generated = computed(() => ({
  ...box.value,
  background: colors.value.background,
  color: colors.value.color,
  fontSize: `${Math.round(props.size * 0.45)}px`,
}))
</script>

<template>
  <img v-if="image" class="app-mark app-mark--image" :style="box" :src="image" alt="" />
  <span v-else class="app-mark" :style="generated" aria-hidden="true">{{ markInitial(name) }}</span>
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
.app-mark--image {
  object-fit: cover;
  background: var(--panel);
}
</style>
