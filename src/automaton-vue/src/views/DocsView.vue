<script setup lang="ts">
import AppNav from '@/components/AppNav.vue'
import AppFooter from '@/components/AppFooter.vue'
</script>

<template>
  <div>
    <AppNav />
    <main class="docs-main">
      <div class="container">
        <h1 style="font-size: 2.5rem; font-weight: 800; margin-bottom: 16px">Documentation</h1>
        <p style="color: var(--color-text-muted); font-size: 1.05rem">Coming soon. Check back later!</p>
      </div>
    </main>
    <AppFooter />
  </div>
</template>

<style scoped>
.docs-main {
  padding: 80px 0;
  min-height: 60vh;
}
</style>
