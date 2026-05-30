<script setup lang="ts">
import { computed, ref } from 'vue'
import { useToast } from 'primevue/usetoast'
import Message from 'primevue/message'
import Button from 'primevue/button'
import { useAuthStore } from '../stores/auth.store'

const authStore = useAuthStore()
const toast = useToast()
const isSending = ref(false)

const shouldShow = computed(() => authStore.isAuthenticated && !!authStore.user && !authStore.user.emailConfirmed)

async function resendConfirmation() {
  isSending.value = true

  try {
    await authStore.resendConfirmation()
    toast.add({
      severity: 'success',
      summary: 'Confirmation Sent',
      detail: 'Check your inbox for a new confirmation email.',
      life: 4000,
    })
  } catch (err: unknown) {
    const errorMessage =
      err && typeof err === 'object' && 'message' in err && typeof (err as Error).message === 'string'
        ? (err as Error).message
        : 'Failed to resend confirmation email.'

    toast.add({
      severity: 'error',
      summary: 'Resend Failed',
      detail: errorMessage,
      life: 4000,
    })
  } finally {
    isSending.value = false
  }
}
</script>

<template>
  <div v-if="shouldShow" class="email-confirmation-banner">
    <Message severity="warn" :closable="false">
      <div class="banner-content">
        <div>
          <div class="banner-title">Please confirm your email</div>
          <div class="banner-text">
            You can explore Studio, but publishing apps, creating API keys, registering runners, and billing actions stay locked until you confirm your email.
          </div>
        </div>
        <Button
          label="Resend Email"
          size="small"
          :loading="isSending"
          :disabled="isSending"
          @click="resendConfirmation"
        />
      </div>
    </Message>
  </div>
</template>

<style scoped>
.email-confirmation-banner {
  padding: 1rem 1rem 0;
}

.banner-content {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  width: 100%;
}

.banner-title {
  font-weight: 700;
  margin-bottom: 0.25rem;
}

.banner-text {
  line-height: 1.45;
}

@media (max-width: 768px) {
  .banner-content {
    align-items: flex-start;
    flex-direction: column;
  }
}
</style>
