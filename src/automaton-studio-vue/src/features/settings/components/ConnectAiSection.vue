<script setup lang="ts">
import { computed } from 'vue'
import Button from 'primevue/button'
import { useToast } from 'primevue/usetoast'

const emit = defineEmits<{ 'open-keys': [] }>()
const toast = useToast()

const apiUrl = (import.meta.env.VITE_API_BASE_URL || window.location.origin).replace(/\/+$/, '')
const mcpAddress = `${apiUrl}/mcp`
const docsUrl = 'https://automaton.monster/docs/work-with-ai'
const cliDocsUrl = 'https://automaton.monster/docs/cli'
const isHttps = mcpAddress.startsWith('https://')
const claudeCodeCommand = `claude mcp add --transport http automaton ${mcpAddress}`
const loginCommand = `automaton login --server ${apiUrl}`
const httpsNote = computed(() => (isHttps ? '' : 'Your server must be reachable over https for Claude to connect.'))

async function copy(text: string, what: string) {
  try {
    await navigator.clipboard.writeText(text)
    toast.add({ severity: 'success', summary: 'Copied', detail: what, life: 2000 })
  } catch {
    toast.add({ severity: 'warn', summary: 'Copy failed', detail: 'Select the text and copy it manually.', life: 4000 })
  }
}
</script>

<template>
  <section class="studio-card connect-ai" data-testid="connect-ai" aria-labelledby="connect-title">
    <h2 id="connect-title" class="card-title">Connect your AI</h2>
    <p class="studio-muted lede">Pick the one you use. Studio asks you to approve each sign-in, so no key is needed.</p>
    <ol class="steps">
      <li>
        <div class="step-text">
          <strong>Claude Code</strong>
          <span class="studio-muted">Run this, then approve the sign-in in Studio.</span>
        </div>
        <div class="command">
          <code data-testid="claude-code-command">{{ claudeCodeCommand }}</code>
          <Button label="Copy" icon="pi pi-copy" size="small" outlined aria-label="Copy command" @click="copy(claudeCodeCommand, 'Command copied')" />
        </div>
      </li>
      <li>
        <div class="step-text">
          <strong>Claude on the web or phone</strong>
          <span class="studio-muted">
            In Claude open Settings, Plugins, Browse plugins, Add connector, then "Or add a custom connector by URL" and paste this address.
            <span v-if="httpsNote" data-testid="https-note">{{ httpsNote }}</span>
          </span>
        </div>
        <div class="command">
          <code data-testid="mcp-address">{{ mcpAddress }}</code>
          <Button label="Copy" icon="pi pi-copy" size="small" outlined aria-label="Copy MCP address" @click="copy(mcpAddress, 'MCP address copied')" />
        </div>
      </li>
      <li>
        <div class="step-text">
          <strong>The Automaton command line</strong>
          <span class="studio-muted">With the CLI installed (<a :href="cliDocsUrl" target="_blank" rel="noopener">CLI guide</a>), sign in and approve it in Studio.</span>
        </div>
        <div class="command">
          <code data-testid="login-command">{{ loginCommand }}</code>
          <Button label="Copy" icon="pi pi-copy" size="small" outlined aria-label="Copy login command" @click="copy(loginCommand, 'Command copied')" />
        </div>
      </li>
    </ol>
    <p class="studio-muted foot">
      <a href="#keys" data-testid="use-key-link" @click.prevent="emit('open-keys')">For scripts and CI: use an API key instead</a>.
      More in <a :href="docsUrl" target="_blank" rel="noopener">Work with AI</a>.
    </p>
  </section>
</template>

<style scoped>
.connect-ai { gap: 12px; }
.card-title { margin: 0; font-family: var(--display); font-size: 16px; font-weight: 600; color: var(--ink); }
.lede, .foot { margin: 0; }
.steps { list-style: none; counter-reset: step; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 14px; }
.steps li { counter-increment: step; position: relative; padding-left: 36px; display: flex; flex-direction: column; gap: 6px; }
.steps li::before {
  content: counter(step);
  position: absolute; left: 0; top: 0;
  width: 24px; height: 24px; border-radius: 50%;
  display: grid; place-items: center;
  border: 1px solid var(--line); color: var(--ink); font-size: 13px; font-weight: 600;
}
.step-text { display: flex; flex-direction: column; font-size: 14px; }
.command {
  display: flex; align-items: center; justify-content: space-between; gap: 8px;
  background: var(--panel); border: 1px solid var(--line); border-radius: 10px;
  padding: 6px 8px 6px 12px; overflow-x: auto;
}
.command code { font-family: var(--mono); font-size: 13px; color: var(--ink); white-space: pre-wrap; word-break: break-all; }
</style>
