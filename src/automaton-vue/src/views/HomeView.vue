<script setup lang="ts">
import AppNav from '@/components/AppNav.vue'
import HeroSection from '@/components/HeroSection.vue'
import HowItWorksSection from '@/components/HowItWorksSection.vue'
import ShowcaseSection from '@/components/ShowcaseSection.vue'
import WhySection from '@/components/WhySection.vue'
import InstallSection from '@/components/InstallSection.vue'
import AppFooter from '@/components/AppFooter.vue'
</script>

<template>
  <div class="home">
    <AppNav />
    <main>
      <HeroSection />
      <ShowcaseSection />
      <HowItWorksSection />
      <WhySection />
      <InstallSection />
    </main>
    <AppFooter />
  </div>
</template>
