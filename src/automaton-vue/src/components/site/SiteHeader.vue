<script setup lang="ts">
import { RouterLink } from 'vue-router'
import { publicAppConfig } from '@/config/publicAppConfig'

const startLink = { path: '/', hash: '#start' }
</script>

<template>
  <header class="band top">
    <div class="wrap">
      <RouterLink class="brand" to="/" aria-label="Automaton home">
        <svg class="mark" viewBox="0 0 40 40" aria-hidden="true">
          <rect x="1.5" y="1.5" width="37" height="37" rx="10" class="mark-bg" />
          <path class="mark-face" d="M10 15l5 4-5 4M30 15l-5 4 5 4M17 27h6" />
        </svg>
        Automaton
      </RouterLink>
      <nav class="nav" aria-label="Main">
        <RouterLink :to="startLink">Get started</RouterLink>
        <RouterLink to="/apps">Apps</RouterLink>
        <a :href="publicAppConfig.docsUrl">Docs</a>
        <a class="gh" :href="publicAppConfig.githubUrl" target="_blank" rel="noopener">
          <svg viewBox="0 0 16 16" aria-hidden="true">
            <path
              d="M8 0C3.58 0 0 3.58 0 8c0 3.54 2.29 6.53 5.47 7.59.4.07.55-.17.55-.38 0-.19-.01-.82-.01-1.49-2.01.37-2.53-.49-2.69-.94-.09-.23-.48-.94-.82-1.13-.28-.15-.68-.52-.01-.53.63-.01 1.08.58 1.23.82.72 1.21 1.87.87 2.33.66.07-.52.28-.87.51-1.07-1.78-.2-3.64-.89-3.64-3.95 0-.87.31-1.59.82-2.15-.08-.2-.36-1.02.08-2.12 0 0 .67-.21 2.2.82.64-.18 1.32-.27 2-.27.68 0 1.36.09 2 .27 1.53-1.04 2.2-.82 2.2-.82.44 1.1.16 1.92.08 2.12.51.56.82 1.27.82 2.15 0 3.07-1.87 3.75-3.65 3.95.29.25.54.73.54 1.48 0 1.07-.01 1.93-.01 2.2 0 .21.15.46.55.38A8.013 8.013 0 0016 8c0-4.42-3.58-8-8-8z"
            />
          </svg>
          GitHub
        </a>
        <RouterLink class="btn small primary" :to="startLink">Install</RouterLink>
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
}
.mark {
  width: 34px;
  height: 34px;
  flex: none;
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
.nav .gh {
  display: inline-flex;
  align-items: center;
  gap: 6px;
}
.nav svg {
  width: 17px;
  height: 17px;
  fill: currentColor;
}

@media (max-width: 620px) {
  .site .nav a:not(.gh):not(.btn) {
    display: none;
  }
}
</style>
