<script setup lang="ts">
import { ref, computed } from 'vue'
import Button from 'primevue/button'
import { useToast } from 'primevue/usetoast'
import { useAuthStore } from '@/features/auth/stores/auth.store'
import { apiKeysService } from '../services/apikeys.service'
import { ApiKeyScope } from '../types/apikeys.types'

const toast = useToast()
const authStore = useAuthStore()

const apiUrl = (import.meta.env.VITE_API_BASE_URL || window.location.origin).replace(/\/+$/, '')
const mcpAddress = `${apiUrl}/mcp`
const docsUrl = 'https://automaton.monster/docs/work-with-ai'

const newKey = ref<string | null>(null)
const creating = ref(false)
const isEmailConfirmed = computed(() => authStore.user?.emailConfirmed ?? false)
const isHttps = mcpAddress.startsWith('https://')

const claudeCodeCommand = computed(
  () =>
    `claude mcp add --transport http automaton ${mcpAddress} --header "Authorization: Bearer ${newKey.value ?? '<your API key>'}"`,
)

async function copy(text: string, what: string) {
  try {
    await navigator.clipboard.writeText(text)
    toast.add({ severity: 'success', summary: 'Copied', detail: what, life: 2000 })
  } catch {
    toast.add({ severity: 'warn', summary: 'Copy failed', detail: 'Select the text and copy it manually.', life: 4000 })
  }
}

async function createKey() {
  creating.value = true
  try {
    const stamp = new Date().toISOString().slice(0, 10)
    const response = await apiKeysService.create({
      name: `Claude Code ${stamp}`,
      description: 'Created from Settings, Connect your AI',
      scope: ApiKeyScope.Developer,
    })
    newKey.value = response.apiKey
  } catch {
    toast.add({ severity: 'error', summary: 'Error', detail: 'Failed to create API key', life: 5000 })
  } finally {
    creating.value = false
  }
}
</script>

<template>
  <div class="connect-ai" data-testid="connect-ai">
    <div class="block">
      <div class="text-sm text-color-secondary mb-1">This server's MCP address</div>
      <div class="command">
        <code data-testid="mcp-address">{{ mcpAddress }}</code>
        <Button icon="pi pi-copy" text rounded size="small" aria-label="Copy MCP address" @click="copy(mcpAddress, 'MCP address copied')" />
      </div>
    </div>

    <div class="block">
      <div class="font-medium mb-1">Claude Code</div>
      <div class="command">
        <code data-testid="claude-code-command">{{ claudeCodeCommand }}</code>
        <Button icon="pi pi-copy" text rounded size="small" aria-label="Copy command" @click="copy(claudeCodeCommand, 'Command copied')" />
      </div>
      <div class="mt-2">
        <Button
          v-if="!newKey"
          label="Create a key"
          icon="pi pi-key"
          size="small"
          severity="secondary"
          :loading="creating"
          :disabled="!isEmailConfirmed"
          @click="createKey"
        />
        <p v-if="!isEmailConfirmed" class="text-sm text-color-secondary m-0 mt-1">Confirm your email to create a key.</p>
        <p v-if="newKey" class="text-sm text-color-secondary m-0" data-testid="key-once">
          This is the only time the key is shown. It is in the command above; copy it now.
        </p>
      </div>
    </div>

    <div class="block">
      <div class="font-medium mb-1">Claude on the web or phone</div>
      <p class="text-sm text-color-secondary m-0">
        In Claude open Settings, Plugins, Browse plugins, Add connector, then "Or add a custom connector by URL" and paste the address above.
        <span v-if="!isHttps" data-testid="https-note">Your server must be reachable over https for Claude to connect.</span>
        <span v-else>The server must be reachable over https; this one is.</span>
      </p>
    </div>

    <p class="text-sm text-color-secondary m-0">
      More in <a :href="docsUrl" target="_blank" rel="noopener">Work with AI</a>.
    </p>
  </div>
</template>

<style scoped>
.connect-ai {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}
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
.command code {
  white-space: pre-wrap;
  word-break: break-all;
}
</style>
