<script setup lang="ts">
import { RouterLink } from 'vue-router'
import { publicAppConfig } from '@/config/publicAppConfig'

// "Automaton App Store" reads as the name "Automaton" with an "App Store" tag, as on the website.
const brandName = publicAppConfig.storeTitle.replace(/\s*App Store$/i, '').trim() || publicAppConfig.storeTitle
const installUrl = 'https://automaton.monster/docs/install-server'
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
        <span class="brand-name">{{ brandName }}</span>
        <span class="tag">App Store</span>
      </RouterLink>
      <nav class="nav" aria-label="Main">
        <RouterLink class="link" to="/">Apps</RouterLink>
        <a class="link" href="https://automaton.monster/docs/">Docs</a>
        <a class="link" href="https://github.com/rzvdaniel/automaton-apps">GitHub</a>
        <span class="actions">
          <a v-if="publicAppConfig.studioUrl" class="btn small" :href="publicAppConfig.studioUrl">Studio</a>
          <a class="btn small primary" :href="installUrl">Get Automaton</a>
        </span>
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
  gap: 16px 22px;
  flex-wrap: wrap;
  padding-block: 16px;
  border-bottom: 1px solid var(--line);
}
.site .brand {
  display: inline-flex;
  align-items: center;
  gap: 10px;
  font: 600 1.1rem var(--display);
  text-decoration: none;
  color: var(--ink);
  min-width: 0;
}
.mark {
  width: 30px;
  height: 30px;
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
.tag {
  font: 500 0.75rem var(--body);
  color: var(--ink-soft);
  padding: 3px 8px;
  border: 1px solid var(--line);
  border-radius: 999px;
  white-space: nowrap;
}
.nav {
  flex: 1;
  display: flex;
  align-items: center;
  gap: 18px;
  font-size: 0.9rem;
}
.site .nav a.link {
  text-decoration: none;
  color: var(--ink-soft);
}
.site .nav a.link:hover,
.site .nav a.router-link-exact-active {
  color: var(--ink);
}
.site .nav a.router-link-exact-active {
  font-weight: 600;
}
.actions {
  margin-left: auto;
  display: flex;
  gap: 10px;
}
@media (max-width: 620px) {
  .nav .link:not(.router-link-exact-active) {
    display: none;
  }
  .top .wrap {
    padding-block: 12px;
  }
  .nav {
    width: 100%;
  }
}
@media (max-width: 400px) {
  .nav .link {
    display: none;
  }
  .actions {
    margin-left: 0;
    width: 100%;
  }
}
</style>
