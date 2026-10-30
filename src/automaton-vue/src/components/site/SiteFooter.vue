<script setup lang="ts">
import { RouterLink } from 'vue-router'
import { publicAppConfig } from '@/config/publicAppConfig'
</script>

<template>
  <footer class="band foot">
    <div class="wrap">
      <span>Automaton · open source under Apache 2.0</span>
      <nav aria-label="Footer">
        <a :href="publicAppConfig.docsUrl">Docs</a>
        <a :href="publicAppConfig.githubUrl" target="_blank" rel="noopener">GitHub</a>
        <a :href="`${publicAppConfig.githubUrl}/releases`" target="_blank" rel="noopener">Releases</a>
        <RouterLink to="/apps">Public apps</RouterLink>
        <a :href="publicAppConfig.exampleAppsUrl" target="_blank" rel="noopener">Example apps</a>
        <a :href="publicAppConfig.llmsUrl">llms.txt</a>
      </nav>
    </div>
  </footer>
</template>

<style scoped>
.foot .wrap {
  padding-block: 36px 48px;
  display: flex;
  flex-wrap: wrap;
  justify-content: space-between;
  gap: 16px;
  color: var(--ink-soft);
  font-size: 0.92rem;
  border-top: 1px solid var(--line);
}
.foot nav {
  display: flex;
  flex-wrap: wrap;
  gap: 18px;
}
</style>
