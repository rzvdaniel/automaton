<script setup lang="ts">
import Button from 'primevue/button'
import { useToast } from 'primevue/usetoast'

const toast = useToast()
const apiUrl = (import.meta.env.VITE_API_BASE_URL || window.location.origin).replace(/\/+$/, '')
const cliDocsUrl = 'https://automaton.monster/docs/automaton-cli'

const steps = [
  { title: 'Install the CLI (needs the .NET 10 SDK)', command: 'dotnet tool install -g Automaton.Cli' },
  { title: 'Sign in to this server (approve it in Studio when the browser opens)', command: `automaton login --server ${apiUrl}` },
]

async function copy(command: string) {
  try {
    await navigator.clipboard.writeText(command)
    toast.add({ severity: 'success', summary: 'Copied', life: 2000 })
  } catch {
    toast.add({ severity: 'warn', summary: 'Copy failed', detail: 'Select the command and copy it manually.', life: 4000 })
  }
}
</script>

<template>
  <div class="cli-steps" data-testid="cli-steps">
    <div v-for="step in steps" :key="step.command" class="mb-2">
      <div class="text-sm text-color-secondary mb-1">{{ step.title }}</div>
      <div class="command">
        <code>{{ step.command }}</code>
        <Button icon="pi pi-copy" text rounded size="small" aria-label="Copy command" @click="copy(step.command)" />
      </div>
    </div>
    <p class="text-sm text-color-secondary m-0">
      Every command is in the <a :href="cliDocsUrl" target="_blank" rel="noopener">CLI guide</a>.
    </p>
  </div>
</template>

<style scoped>
.command {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.5rem;
  background: var(--p-content-hover-background);
  border-radius: var(--p-content-border-radius);
  padding: 0.25rem 0.5rem 0.25rem 0.75rem;
  overflow-x: auto;
}
</style>
