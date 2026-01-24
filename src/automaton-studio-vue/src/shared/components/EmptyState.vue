<script setup lang="ts">
defineProps<{
  icon?: string
  title: string
  message: string
}>()
</script>

<template>
  <div class="empty-state text-center py-8">
    <div v-if="icon" class="mb-4">
      <i :class="[icon, 'text-4xl', 'text-color-secondary']"></i>
    </div>
    <h3 class="text-xl font-semibold mb-2">{{ title }}</h3>
    <p class="text-color-secondary mb-6">{{ message }}</p>
    <slot />
  </div>
</template>

<style scoped>
.empty-state {
  padding: 2rem 1rem;
}
</style>
