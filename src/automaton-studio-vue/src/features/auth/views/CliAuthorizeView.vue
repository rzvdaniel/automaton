<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import Card from 'primevue/card'
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
  <div class="cli-authorize-view flex align-items-center justify-content-center min-h-screen p-4">
    <Card class="w-full" style="max-width: 500px">
      <template #title>Authorize Automaton CLI</template>
      <template #content>
        <Message v-if="error" severity="error" :closable="false" class="mb-4">{{ error }}</Message>
        <Message v-if="approved" severity="success" :closable="false" class="mb-4">
          Automaton CLI is authorized on this device. You can return to the terminal.
        </Message>

        <template v-if="!approved">
          <p class="line-height-3">
            Approve this request to create a Developer credential for the Automaton CLI.
            The credential will be shown only to the waiting CLI process.
          </p>
          <div class="surface-100 border-round p-3 mb-4">
            <div class="text-color-secondary text-sm mb-1">Authorization code</div>
            <div class="text-2xl font-bold letter-spacing-2">{{ userCode || 'Unavailable' }}</div>
          </div>
          <div v-if="details" class="border-1 surface-border border-round p-3 mb-4">
            <div class="flex justify-content-between mb-2"><span class="text-color-secondary">Device</span><span>{{ details.deviceName }}</span></div>
            <div class="flex justify-content-between mb-2"><span class="text-color-secondary">Requested from</span><span>{{ details.remoteIp || 'Not available' }}</span></div>
            <div class="flex justify-content-between mb-2"><span class="text-color-secondary">Requested time</span><span>{{ formatDate(details.createdAt) }}</span></div>
            <div class="flex justify-content-between"><span class="text-color-secondary">Credential</span><span>Developer / CLI</span></div>
          </div>
          <div class="flex gap-2">
            <Button label="Cancel" severity="secondary" outlined :loading="cancelling" class="flex-1" @click="cancel" />
            <Button label="Authorize Automaton CLI" icon="pi pi-check" :loading="approving || loadingDetails" :disabled="loadingDetails || !details" class="flex-1" @click="approve" />
          </div>
        </template>
        <Button v-else label="Return to Studio" severity="secondary" class="w-full" @click="returnToDashboard" />
      </template>
    </Card>
  </div>
</template>
