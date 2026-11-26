<script setup lang="ts">
import { RouterLink } from 'vue-router'
import logoUrl from '@/assets/logo.svg'
import { publicAppConfig } from '@/config/publicAppConfig'
</script>

<template>
  <footer>
    <div class="wrap">
      <div>
        <span class="logo"><img class="logo-mark" :src="logoUrl" alt="" width="28" height="28" />Automaton</span>
        <p style="margin-top: 10px">Open-source app platform for the apps your AI builds. Apache 2.0.</p>
      </div>
      <div>
        <b>Product</b>
        <RouterLink to="/product">Overview</RouterLink>
        <RouterLink to="/use-cases">Use cases</RouterLink>
        <a :href="publicAppConfig.storeUrl">Apps</a>
        <RouterLink to="/catalog">Catalog</RouterLink>
        <RouterLink to="/roadmap">Roadmap</RouterLink>
        <RouterLink to="/spec">Spec</RouterLink>
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
        <a href="/docs/what-is-automaton">What is Automaton</a>
        <a :href="`${publicAppConfig.githubUrl}/security/policy`" target="_blank" rel="noopener">Security policy</a>
      </div>
    </div>
  </footer>
</template>
