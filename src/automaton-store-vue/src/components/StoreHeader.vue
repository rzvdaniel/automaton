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
        <a href="https://automaton.monster/">Automaton</a>
        <a class="github-link" href="https://github.com/rzvdaniel/automaton-apps" aria-label="Automaton apps on GitHub" title="Automaton apps on GitHub">
          <svg viewBox="0 0 24 24" aria-hidden="true" width="22" height="22" fill="currentColor">
            <path d="M12 .75a11.25 11.25 0 0 0-3.56 21.92c.56.1.77-.24.77-.54v-2.1c-3.13.68-3.79-1.33-3.79-1.33-.51-1.3-1.25-1.65-1.25-1.65-1.02-.7.08-.69.08-.69 1.13.08 1.73 1.16 1.73 1.16 1 1.72 2.63 1.22 3.27.93.1-.73.4-1.22.71-1.5-2.5-.29-5.13-1.25-5.13-5.56 0-1.23.44-2.23 1.16-3.02-.12-.29-.5-1.43.11-2.98 0 0 .95-.3 3.09 1.15a10.76 10.76 0 0 1 5.62 0c2.15-1.45 3.09-1.15 3.09-1.15.61 1.55.23 2.69.11 2.98.72.79 1.16 1.79 1.16 3.02 0 4.32-2.63 5.27-5.14 5.55.4.35.76 1.03.76 2.08v3.11c0 .3.2.65.77.54A11.25 11.25 0 0 0 12 .75Z" />
          </svg>
        </a>
        <a v-if="publicAppConfig.studioUrl" class="btn small" :href="publicAppConfig.studioUrl">Studio</a>
      </nav>
    </div>
  </header>
</template>

<style scoped>
/* `.site` supplies the theme on every page; only page roots fill the viewport. */
.top.site {
  min-height: 0;
}
.top .wrap {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  flex-wrap: wrap;
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
  flex-wrap: wrap;
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
.github-link {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 44px;
  min-height: 44px;
}
@media (max-width: 620px) {
  .nav {
    width: 100%;
    gap: 18px;
  }
}
</style>
