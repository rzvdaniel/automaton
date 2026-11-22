<script setup lang="ts">
import { RouterLink } from 'vue-router'
import { publicAppConfig } from '@/config/publicAppConfig'
import { SHOW_PREVIEW_BADGE } from '@/config/preview'
import { buildSiteNav } from '@site-nav/siteNav'
import logoUrl from '@/assets/logo.svg'

const navItems = buildSiteNav(publicAppConfig)
</script>

<template>
  <nav class="top" aria-label="Main">
    <div class="wrap">
      <RouterLink class="logo" to="/" aria-label="Automaton home"><img class="logo-mark" :src="logoUrl" alt="" width="28" height="28" />Automaton</RouterLink>
      <RouterLink v-if="SHOW_PREVIEW_BADGE" class="preview-pill" :to="{ path: '/', hash: '#preview' }">Preview</RouterLink>
      <ul>
        <li v-for="item in navItems" :key="item.key">
          <RouterLink v-if="item.path" :to="item.path">{{ item.label }}</RouterLink>
          <a v-else :href="item.href" :target="item.external ? '_blank' : undefined" :rel="item.external ? 'noopener' : undefined">{{ item.label }}</a>
        </li>
      </ul>
      <div class="right">
        <RouterLink class="btn" to="/how-setup-works">How setup works</RouterLink>
        <RouterLink class="btn primary" to="/get-started">Get started</RouterLink>
      </div>
    </div>
  </nav>
  <div class="subnav">
    <div class="wrap">
      <RouterLink to="/">Home</RouterLink>
      <template v-for="item in navItems" :key="item.key">
        <RouterLink v-if="item.path" :to="item.path">{{ item.label }}</RouterLink>
        <a v-else :href="item.href" :target="item.external ? '_blank' : undefined" :rel="item.external ? 'noopener' : undefined">{{ item.label }}</a>
      </template>
    </div>
  </div>
</template>
