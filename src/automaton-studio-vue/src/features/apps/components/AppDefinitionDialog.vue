<script setup lang="ts">
import { computed } from 'vue'
import Dialog from 'primevue/dialog'
import type { App } from '../types/apps.types'

const props = defineProps<{ app: App }>()
const visible = defineModel<boolean>('visible', { required: true })
const json = computed(() => JSON.stringify(props.app.application, null, 2))
</script>

<template>
  <Dialog v-model:visible="visible" modal header="App definition (automaton.json)" :style="{ width: 'min(40rem, 94vw)' }">
    <pre class="code" data-testid="raw-contract">{{ json }}</pre>
  </Dialog>
</template>

<style scoped>
.code { max-height: 60vh; overflow: auto; margin: 0; font-family: var(--mono); font-size: 12px; }
</style>
