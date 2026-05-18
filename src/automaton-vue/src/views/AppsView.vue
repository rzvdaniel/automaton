<script setup lang="ts">
import AppsDiscoveryStrip from '@/components/AppsDiscoveryStrip.vue'
import AppFooter from '@/components/AppFooter.vue'
import PublicSiteHeader from '@/components/PublicSiteHeader.vue'
import ShowcaseSection from '@/components/ShowcaseSection.vue'
</script>

<template>
  <div>
    <PublicSiteHeader />
    <AppsDiscoveryStrip />
    <main>
      <ShowcaseSection full-page />
    </main>
    <AppFooter />
  </div>
</template>
