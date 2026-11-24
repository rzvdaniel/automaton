<script setup lang="ts">
import { RouterLink } from 'vue-router'
import BHeader from '@/components/site/b/BHeader.vue'
import BFooter from '@/components/site/b/BFooter.vue'
import { publicAppConfig } from '@/config/publicAppConfig'
import { useSiteTheme } from '@/composables/useSiteTheme'
import '@/assets/site-b.css'

useSiteTheme()
</script>

<template>
  <div class="site b">
    <BHeader />
    <main>
      <header class="hero">
        <div class="wrap">
          <div>
            <p class="eyebrow">404</p>
            <h1>Page not found</h1>
            <p class="lede">The page you are looking for does not exist or has moved.</p>
            <div class="row">
              <RouterLink class="btn primary" to="/">Home</RouterLink>
              <a class="btn" :href="publicAppConfig.docsUrl">Docs</a>
            </div>
          </div>
        </div>
      </header>
    </main>
    <BFooter />
  </div>
</template>
