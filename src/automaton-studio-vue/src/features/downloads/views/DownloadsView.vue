<template>
  <div class="cli-page">
    <h1>Automaton CLI</h1>
    <p class="text-color-secondary">
      Install the CLI on your development computer, then build, deploy and publish apps to this server.
      It requires the .NET 9 SDK or later.
    </p>

    <Card v-for="step in steps" :key="step.title" class="mb-3">
      <template #title>{{ step.title }}</template>
      <template #content>
        <p v-if="step.note" class="mt-0 text-color-secondary">{{ step.note }}</p>
        <div class="command">
          <pre><code>{{ step.command }}</code></pre>
          <Button icon="pi pi-copy" text rounded aria-label="Copy command" @click="copy(step.command)" />
        </div>
      </template>
    </Card>

    <p class="text-color-secondary">
      Runners are installed from <RouterLink to="/runners">Runners</RouterLink>. See the
      <a :href="cliDocsUrl" target="_blank" rel="noopener">CLI guide</a> for every command.
    </p>
  </div>
</template>

<script setup lang="ts">
import Button from 'primevue/button'
import Card from 'primevue/card'
import { useToast } from 'primevue/usetoast'

const toast = useToast()
const apiUrl = (import.meta.env.VITE_API_BASE_URL || window.location.origin).replace(/\/+$/, '')
const cliDocsUrl = 'https://automaton.monster/docs/automaton-cli'

const steps = [
  { title: '1. Install', command: 'dotnet tool install -g Automaton.Cli' },
  { title: '2. Sign in to this server', note: 'Approve the request in Studio when your browser opens.', command: `automaton login --server ${apiUrl}` },
  { title: '3. Create, deploy and publish an app', command: 'mkdir my-app && cd my-app\nautomaton new "My App"\nautomaton validate\nautomaton deploy\nautomaton publish --yes --slug my-app' },
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

<style scoped>
.cli-page {
  max-width: 48rem;
}
.command {
  display: flex;
  align-items: flex-start;
  gap: 0.5rem;
  background: var(--p-content-hover-background);
  border-radius: var(--p-content-border-radius);
  padding: 0.75rem 1rem;
}
.command pre {
  flex: 1;
  margin: 0;
  overflow-x: auto;
  white-space: pre;
}
</style>
