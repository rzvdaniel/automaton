<script setup lang="ts">
import Button from 'primevue/button'
import Dialog from 'primevue/dialog'
import CopyCommand from './CopyCommand.vue'

const visible = defineModel<boolean>('visible', { required: true })
const emit = defineEmits<{ catalog: [] }>()

const prompt =
  'Create a new app on my Automaton server with the Automaton CLI. Read https://automaton.monster/llms.txt first, ' +
  'then run: automaton new "My App", automaton validate, automaton deploy. Ask me what the app should do.'

function openCatalog() {
  visible.value = false
  emit('catalog')
}
</script>

<template>
  <Dialog v-model:visible="visible" modal header="New app" :style="{ width: '34rem', maxWidth: '95vw' }" data-testid="new-app-dialog">
    <section class="path">
      <h3>Ask your AI</h3>
      <p>Paste this into your AI agent. It builds and deploys the app with the CLI.</p>
      <CopyCommand class="box" :command="prompt" button-label="Copy prompt" />
      <p class="small">
        First time? Install and approve the CLI, then the agent can work:
      </p>
      <CopyCommand class="box" command="dotnet tool install -g Automaton.Cli" />
      <CopyCommand class="box" command="automaton login" />
      <p class="small">
        Studio asks you to approve the login. Approved agents and API keys are listed under
        <RouterLink to="/settings#agents" @click="visible = false">Settings</RouterLink>.
        <a href="https://automaton.monster/docs/automaton-cli" target="_blank" rel="noopener">CLI guide</a>
      </p>
    </section>

    <section class="path">
      <h3>Pick from the catalog</h3>
      <p>Install a ready-made app in a few clicks.</p>
      <Button label="Open the catalog" outlined @click="openCatalog" />
    </section>
  </Dialog>
</template>

<style scoped>
.path { margin-bottom: 1.25rem; }
.path h3 { margin: 0 0 0.25rem; font-size: 1rem; }
.path p { margin: 0 0 0.5rem; font-size: 0.9rem; color: var(--p-text-muted-color); }
.small { font-size: 0.8rem !important; }
.box { margin-bottom: 0.4rem; }
</style>
