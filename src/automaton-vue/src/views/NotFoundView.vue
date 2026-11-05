<script setup lang="ts">
import AppFooter from '@/components/AppFooter.vue'
import PublicSiteHeader from '@/components/PublicSiteHeader.vue'
</script>

<template>
  <div class="not-found-page">
    <PublicSiteHeader />
    <main class="not-found-main">
      <div class="shell">
        <span class="eyebrow">404</span>
        <h1>Page not found</h1>
        <p>The page you are looking for does not exist or has moved.</p>
        <RouterLink to="/" class="home-link">Back to the home page</RouterLink>
      </div>
    </main>
    <AppFooter />
  </div>
</template>

<style scoped>
.not-found-page {
  min-height: 100vh;
  display: flex;
  flex-direction: column;
  background: linear-gradient(180deg, #090b10 0%, #07080b 44%, #050608 100%);
  color: #f5f7fb;
}

.shell {
  width: min(1260px, calc(100% - 40px));
  margin: 0 auto;
}

.not-found-main {
  flex: 1;
  padding: 84px 0;
}

.eyebrow {
  display: inline-flex;
  padding: 5px 10px;
  border: 1px solid rgba(80, 215, 255, 0.22);
  border-radius: 999px;
  background: rgba(80, 215, 255, 0.08);
  color: #c4f2ff;
  font-size: 0.78rem;
  font-weight: 850;
}

h1 {
  margin: 16px 0 12px;
  font-size: clamp(2.3rem, 5vw, 4rem);
  letter-spacing: -0.05em;
}

p {
  margin: 0 0 24px;
  color: #c8ceda;
  font-size: 1.06rem;
}

.home-link {
  color: #c4f2ff;
  font-weight: 700;
}
</style>
