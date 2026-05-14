<script setup lang="ts">
import AppNav from '@/components/AppNav.vue'
import AppFooter from '@/components/AppFooter.vue'
import ShowcaseSection from '@/components/ShowcaseSection.vue'
</script>

<template>
  <div>
    <AppNav />
    <main style="padding-top: 40px">
      <ShowcaseSection full-page />
    </main>
    <AppFooter />
  </div>
</template>
