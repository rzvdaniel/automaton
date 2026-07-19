<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import Card from 'primevue/card'
import Button from 'primevue/button'
import Message from 'primevue/message'
import { approveCliAuthorization } from '../services/cli-authorization.service'

const route = useRoute()
const router = useRouter()
const requestId = computed(() => typeof route.query.requestId === 'string' ? route.query.requestId : '')
const userCode = computed(() => typeof route.query.code === 'string' ? route.query.code : '')
const approving = ref(false)
const approved = ref(false)
const error = ref('')

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
          <Button label="Authorize Automaton CLI" icon="pi pi-check" :loading="approving" class="w-full" @click="approve" />
        </template>
        <Button v-else label="Return to Studio" severity="secondary" class="w-full" @click="returnToDashboard" />
      </template>
    </Card>
  </div>
</template>
