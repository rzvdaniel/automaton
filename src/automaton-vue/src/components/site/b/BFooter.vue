<script setup lang="ts">
import { RouterLink } from 'vue-router'
import { publicAppConfig } from '@/config/publicAppConfig'
</script>

<template>
  <footer>
    <div class="wrap">
      <div>
        <span class="logo"><i>&gt;_</i>Automaton</span>
        <p style="margin-top: 10px">Open-source app platform for the apps your AI builds. Apache 2.0.</p>
      </div>
      <div>
        <b>Product</b>
        <RouterLink to="/product">Overview</RouterLink>
        <RouterLink to="/use-cases">Use cases</RouterLink>
        <a :href="publicAppConfig.storeUrl">Apps</a>
        <RouterLink to="/catalog">Catalog</RouterLink>
      </div>
      <div>
        <b>Start</b>
        <RouterLink to="/how-setup-works">How setup works</RouterLink>
        <RouterLink to="/get-started">Get started</RouterLink>
        <RouterLink to="/cli">CLI</RouterLink>
        <a :href="publicAppConfig.llmsUrl">llms.txt</a>
      </div>
      <div>
        <b>Project</b>
        <a :href="publicAppConfig.docsUrl">Docs</a>
        <a :href="publicAppConfig.githubUrl" target="_blank" rel="noopener">GitHub</a>
        <a :href="`${publicAppConfig.githubUrl}/releases`" target="_blank" rel="noopener">Releases</a>
        <a href="/docs/numbers">Numbers</a>
      </div>
    </div>
  </footer>
</template>
