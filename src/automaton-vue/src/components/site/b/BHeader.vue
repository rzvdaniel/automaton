<script setup lang="ts">
import { RouterLink } from 'vue-router'
import { publicAppConfig } from '@/config/publicAppConfig'
</script>

<template>
  <nav class="top" aria-label="Main">
    <div class="wrap">
      <RouterLink class="logo" to="/" aria-label="Automaton home"><i>&gt;_</i>Automaton</RouterLink>
      <ul>
        <li><RouterLink to="/product">Product</RouterLink></li>
        <li><RouterLink to="/use-cases">Use cases</RouterLink></li>
        <li><a :href="publicAppConfig.storeUrl">Apps</a></li>
        <li><RouterLink to="/catalog">Catalog</RouterLink></li>
        <li><a :href="publicAppConfig.docsUrl">Docs</a></li>
        <li><a :href="publicAppConfig.githubUrl" target="_blank" rel="noopener">GitHub</a></li>
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
      <RouterLink to="/product">Product</RouterLink>
      <RouterLink to="/use-cases">Use cases</RouterLink>
      <a :href="publicAppConfig.storeUrl">Apps</a>
      <RouterLink to="/catalog">Catalog</RouterLink>
      <RouterLink to="/how-setup-works">How setup works</RouterLink>
      <a :href="publicAppConfig.docsUrl">Docs</a>
    </div>
  </div>
</template>
