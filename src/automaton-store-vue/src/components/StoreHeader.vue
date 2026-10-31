<script setup lang="ts">
import { RouterLink } from 'vue-router'
import { publicAppConfig } from '@/config/publicAppConfig'
</script>

<template>
  <header class="site band top">
    <div class="wrap">
      <RouterLink class="brand" to="/" :aria-label="`${publicAppConfig.storeTitle} home`">
        <img v-if="publicAppConfig.logoUrl" class="mark" :src="publicAppConfig.logoUrl" alt="" />
        <svg v-else class="mark" viewBox="0 0 40 40" aria-hidden="true">
          <rect x="1.5" y="1.5" width="37" height="37" rx="10" class="mark-bg" />
          <path class="mark-face" d="M10 15l5 4-5 4M30 15l-5 4 5 4M17 27h6" />
        </svg>
        {{ publicAppConfig.storeTitle }}
      </RouterLink>
      <nav class="nav" aria-label="Main">
        <RouterLink to="/">Apps</RouterLink>
        <a v-if="publicAppConfig.studioUrl" class="btn small" :href="publicAppConfig.studioUrl">Studio</a>
      </nav>
    </div>
  </header>
</template>

<style scoped>
.top .wrap {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  padding-block: 22px;
}
.site .brand {
  display: inline-flex;
  align-items: center;
  gap: 10px;
  font: 700 1.2rem var(--display);
  text-decoration: none;
  color: var(--ink);
  min-width: 0;
}
.mark {
  width: 34px;
  height: 34px;
  flex: none;
  object-fit: contain;
}
.mark-bg {
  fill: var(--ink);
}
.mark-face {
  fill: none;
  stroke: var(--signal);
  stroke-width: 2.6;
  stroke-linecap: round;
  stroke-linejoin: round;
}
.nav {
  display: flex;
  align-items: center;
  gap: 22px;
  font-size: 0.95rem;
}
.site .nav a:not(.btn) {
  text-decoration: none;
  color: var(--ink-soft);
}
.site .nav a:not(.btn):hover,
.site .nav a.router-link-exact-active:not(.btn) {
  color: var(--ink);
}
</style>
