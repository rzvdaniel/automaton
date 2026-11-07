<script setup lang="ts">
import Button from 'primevue/button'
import { useRouter } from 'vue-router'

const router = useRouter()
</script>

<template>
  <div class="not-found" data-testid="not-found">
    <h1>Page not found</h1>
    <p>This page does not exist in Studio.</p>
    <Button label="Go to Apps" icon="pi pi-arrow-left" @click="router.push({ name: 'apps' })" />
  </div>
</template>

<style scoped>
.not-found {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 0.75rem;
  padding: 2rem;
}

.not-found h1 {
  margin: 0;
}

.not-found p {
  margin: 0;
  color: var(--p-text-muted-color);
}
</style>
