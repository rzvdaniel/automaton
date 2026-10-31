<script setup lang="ts">
import { publicAppConfig } from '@/config/publicAppConfig'
</script>

<template>
  <footer class="site band foot">
    <div class="wrap">
      <span>{{ publicAppConfig.storeTitle }}</span>
      <nav aria-label="Footer">
        <a v-if="publicAppConfig.studioUrl" :href="publicAppConfig.studioUrl">Studio</a>
        <a href="https://automaton.monster" target="_blank" rel="noopener">Powered by Automaton</a>
      </nav>
    </div>
  </footer>
</template>

<style scoped>
/* `.site` supplies the theme on every page; only page roots fill the viewport. */
.foot.site {
  min-height: 0;
}
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
