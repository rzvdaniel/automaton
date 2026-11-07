<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import AuthCard from '../components/AuthCard.vue'
import Button from 'primevue/button'
import Message from 'primevue/message'
import { approveCliAuthorization, cancelCliAuthorization, getCliAuthorizationDetails, type CliAuthorizationDetails } from '../services/cli-authorization.service'

const route = useRoute()
const router = useRouter()
const requestId = computed(() => typeof route.query.requestId === 'string' ? route.query.requestId : '')
const userCode = computed(() => typeof route.query.code === 'string' ? route.query.code : '')
const approving = ref(false)
const approved = ref(false)
const error = ref('')
const loadingDetails = ref(true)
const details = ref<CliAuthorizationDetails | null>(null)
const cancelling = ref(false)

onMounted(async () => {
  if (!requestId.value || !userCode.value) {
    error.value = 'This authorization link is incomplete. Restart automaton login.'
    loadingDetails.value = false
    return
  }

  try {
    details.value = await getCliAuthorizationDetails(requestId.value, userCode.value)
  } catch {
    error.value = 'This authorization request is invalid or expired.'
  } finally {
    loadingDetails.value = false
  }
})

async function approve() {
  if (!requestId.value || !userCode.value) {
    error.value = 'This authorization link is incomplete. Restart automaton login.'
    return
  }

  approving.value = true
  error.value = ''
  try {
    await approveCliAuthorization(requestId.value, userCode.value)
    approved.value = true
  } catch (err: unknown) {
    const response = (err as { response?: { data?: { message?: string } } }).response
    error.value = response?.data?.message ?? 'This authorization request is invalid or expired.'
  } finally {
    approving.value = false
  }
}

function returnToDashboard() {
  void router.push('/')
}

function cancel() {
  if (!requestId.value || !userCode.value) {
    void router.push('/')
    return
  }

  cancelling.value = true
  void cancelCliAuthorization(requestId.value, userCode.value)
    .catch(() => { error.value = 'The authorization request could not be cancelled.' })
    .finally(() => {
      cancelling.value = false
      if (!error.value) void router.push('/')
    })
}

function formatDate(value?: string) {
  return value ? new Date(value).toLocaleString() : 'Unavailable'
}
</script>

<template>
  <AuthCard
    title="Let your AI agent in?"
    :lead="approved ? undefined : 'An AI agent is asking to use Automaton as you. Check that this code matches the one it shows.'"
  >
    <Message v-if="error" severity="error" :closable="false" class="mb-4">{{ error }}</Message>
    <Message v-if="approved" severity="success" :closable="false" class="mb-4">
      Automaton CLI is authorized on this device. You can return to the terminal.
    </Message>

    <template v-if="!approved">
      <div class="cli-code" aria-label="Authorization code">
        <span v-if="!userCode" class="auth-note">Unavailable</span>
        <template v-for="(char, index) in userCode.split('')" :key="index">
          <span v-if="char === '-'" class="cli-code-sep">-</span>
          <span v-else class="cli-code-char">{{ char }}</span>
        </template>
      </div>
      <p class="auth-note" style="margin-bottom: 8px">Approving lets the agent, through the Automaton CLI:</p>
      <ul class="cli-list">
        <li>Create, change, deploy and publish apps as you.</li>
        <li>Work with the apps and organizations you can already reach.</li>
      </ul>
      <p class="auth-note" style="margin-bottom: 16px">
        It does not get API-key management, administration, billing or infrastructure enrollment.
        The credential is shown only to the waiting CLI process.
      </p>
      <div v-if="details" class="cli-details">
        <div><span>Device</span><span>{{ details.deviceName }}</span></div>
        <div><span>Requested from</span><span>{{ details.remoteIp || 'Not available' }}</span></div>
        <div><span>Requested time</span><span>{{ formatDate(details.createdAt) }}</span></div>
        <div><span>Credential</span><span>Developer / CLI</span></div>
      </div>
      <div class="cli-actions">
        <Button label="Deny" class="auth-outline" :loading="cancelling" @click="cancel" />
        <Button label="Approve" class="auth-signal" :loading="approving || loadingDetails" :disabled="loadingDetails || !details" @click="approve" />
      </div>
      <p class="auth-note">
        Remove this access later under <router-link to="/apikeys" class="auth-link auth-link--inline">API Keys</router-link>.
      </p>
    </template>
    <Button v-else label="Return to Studio" class="auth-outline" @click="returnToDashboard" />
  </AuthCard>
</template>
