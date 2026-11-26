<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import AuthCard from '../components/AuthCard.vue'
import Button from 'primevue/button'
import Message from 'primevue/message'
import { decideMcpOAuthRequest, describeMcpOAuthRequest, type McpOAuthParams } from '../services/mcp-oauth.service'

const route = useRoute()
const loading = ref(true)
const working = ref(false)
const error = ref('')
const details = ref<{ clientName: string; redirectHost: string } | null>(null)

function param(name: string): string {
  const value = route.query[name]
  return typeof value === 'string' ? value : ''
}

const params = computed<McpOAuthParams>(() => ({
  client_id: param('client_id'),
  redirect_uri: param('redirect_uri'),
  code_challenge: param('code_challenge'),
  code_challenge_method: param('code_challenge_method'),
  state: param('state') || undefined,
}))

onMounted(async () => {
  try {
    details.value = await describeMcpOAuthRequest(params.value)
  } catch {
    error.value = 'This sign-in link is invalid or expired. Remove the connector and add it again.'
  } finally {
    loading.value = false
  }
})

async function decide(decision: 'approve' | 'deny') {
  working.value = true
  error.value = ''
  try {
    window.location.assign(await decideMcpOAuthRequest(decision, params.value))
  } catch (err: unknown) {
    const response = (err as { response?: { data?: { message?: string } } }).response
    error.value = response?.data?.message ?? 'This sign-in link is invalid or expired.'
    working.value = false
  }
}
</script>

<template>
  <AuthCard
    title="Let Claude manage your apps?"
    lead="An AI connector is asking to sign in to this Automaton server as you."
  >
    <Message v-if="error" severity="error" :closable="false" class="mb-4">{{ error }}</Message>
    <template v-if="details">
      <div class="cli-details">
        <div><span>Connector (named by itself)</span><span>{{ details.clientName }}</span></div>
        <div><span>Returns to</span><span>{{ details.redirectHost }}</span></div>
      </div>
      <p class="auth-note" style="margin-bottom: 8px">Approving lets it, as you:</p>
      <ul class="cli-list">
        <li>Read your apps, their activity and logs.</li>
        <li>Create, deploy, share and pause apps.</li>
      </ul>
      <p class="auth-note" style="margin-bottom: 16px">
        It has the same rights as the Automaton CLI and none of an administrator's. It cannot delete apps.
        The access expires after an hour and renews while the connector is used.
      </p>
      <div class="cli-actions">
        <Button label="Deny" class="auth-outline" :disabled="working" @click="decide('deny')" />
        <Button label="Approve" class="auth-signal" :loading="working" @click="decide('approve')" />
      </div>
      <p class="auth-note">
        Remove this access later under <router-link to="/settings#keys" class="auth-link auth-link--inline">API Keys</router-link>.
      </p>
    </template>
    <p v-else-if="loading" class="auth-note">Checking the sign-in request…</p>
  </AuthCard>
</template>
