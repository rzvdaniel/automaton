<script setup lang="ts">
import { RouterLink } from 'vue-router'
import { publicAppConfig } from '@/config/publicAppConfig'
import { buildSiteNav } from '@site-nav/siteNav'
import logoUrl from '@site-nav/logo.svg'

// The same menu as automaton.monster; this store is the "Live apps" page.
const siteUrl = 'https://automaton.monster'
const navItems = buildSiteNav({
  storeUrl: '/',
  docsUrl: `${siteUrl}/docs/`,
  githubUrl: 'https://github.com/rzvdaniel/automaton',
}).map((item) => (item.path ? { ...item, href: `${siteUrl}${item.path}`, path: undefined } : item))
const getStartedUrl = `${siteUrl}/get-started`
</script>

<template>
  <header class="site band top">
    <div class="wrap">
      <a class="brand" :href="`${siteUrl}/`" aria-label="Automaton home">
        <img class="mark" :src="publicAppConfig.logoUrl || logoUrl" alt="" />
        <span class="brand-name">Automaton</span>
      </a>
      <nav class="nav" aria-label="Main">
        <template v-for="item in navItems" :key="item.key">
          <RouterLink v-if="item.key === 'live-apps'" class="link" to="/" aria-current="page">{{ item.label }}</RouterLink>
          <a v-else class="link" :href="item.href" :target="item.external ? '_blank' : undefined" :rel="item.external ? 'noopener' : undefined">{{ item.label }}</a>
        </template>
        <span class="actions">
          <a v-if="publicAppConfig.studioUrl" class="btn small" :href="publicAppConfig.studioUrl">Studio</a>
          <a class="btn small primary" :href="getStartedUrl">Get started</a>
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
.site .nav a[aria-current='page'] {
  color: var(--ink);
}
.site .nav a[aria-current='page'] {
  font-weight: 600;
}
.actions {
  margin-left: auto;
  display: flex;
  gap: 10px;
}
@media (max-width: 620px) {
  .top .wrap {
    padding-block: 12px;
  }
  .nav {
    width: 100%;
  }
}
@media (max-width: 620px) {
  .nav {
    flex-wrap: wrap;
    gap: 10px 16px;
  }
}
@media (max-width: 400px) {
  .actions {
    margin-left: 0;
    width: 100%;
  }
}
</style>
