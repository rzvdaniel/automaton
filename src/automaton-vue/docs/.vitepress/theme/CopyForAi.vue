<script setup lang="ts">
import { ref } from 'vue'
import { useData, withBase } from 'vitepress'

const { page } = useData()
const label = ref('Copy for AI')

async function copyPage() {
  try {
    const response = await fetch(withBase(`/${page.value.relativePath}`))
    if (!response.ok) throw new Error(String(response.status))
    await navigator.clipboard.writeText(await response.text())
    label.value = 'Copied'
  } catch {
    label.value = 'Copy failed'
  }
  setTimeout(() => (label.value = 'Copy for AI'), 2000)
}
</script>

<template>
  <button v-if="page.relativePath !== 'index.md'" type="button" class="copy-for-ai" @click="copyPage">
    {{ label }}
  </button>
</template>

<style scoped>
.copy-for-ai {
  float: right;
  margin-bottom: 8px;
  padding: 4px 12px;
  border: 1px solid var(--vp-c-divider);
  border-radius: 6px;
  background: var(--vp-c-bg-soft);
  color: var(--vp-c-text-2);
  font-size: 13px;
  cursor: pointer;
}
.copy-for-ai:hover {
  color: var(--vp-c-brand-1);
  border-color: var(--vp-c-brand-1);
}
</style>
