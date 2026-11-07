<script setup lang="ts">
import { ref } from 'vue'
import AuthCard from '../components/AuthCard.vue'
import InputText from 'primevue/inputtext'
import Button from 'primevue/button'
import Message from 'primevue/message'
import CloudflareTurnstile from '@/shared/components/CloudflareTurnstile.vue'
import { authService } from '../services/auth.service'
import type { ForgotPasswordFormErrors } from '../types/auth.types'

// Form state
const email = ref('')
const formErrors = ref<ForgotPasswordFormErrors>({})
const submitted = ref(false)
const isLoading = ref(false)
const error = ref<string | null>(null)
const success = ref(false)
const turnstileToken = ref('')
const turnstileError = ref<string | null>(null)
const turnstileRef = ref<InstanceType<typeof CloudflareTurnstile> | null>(null)
const turnstileSiteKey = import.meta.env.VITE_TURNSTILE_SITE_KEY || ''
const turnstileEnabled =
  import.meta.env.VITE_TURNSTILE_ENABLED === 'true' && !!turnstileSiteKey

// Form validation
function validateForm(): boolean {
  formErrors.value = {}
  let isValid = true

  if (!email.value) {
    formErrors.value.email = 'Email is required'
    isValid = false
  } else if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email.value)) {
    formErrors.value.email = 'Please enter a valid email address'
    isValid = false
  }

  if (turnstileEnabled && !turnstileToken.value) {
    turnstileError.value = 'Please complete the verification challenge'
    isValid = false
  }

  return isValid
}

// Handle form submission
async function handleSubmit() {
  submitted.value = true
  error.value = null
  turnstileError.value = null

  if (!validateForm()) {
    return
  }

  isLoading.value = true

  try {
    await authService.forgotPassword({
      email: email.value,
      turnstileToken: turnstileToken.value || undefined,
    })
    success.value = true
  } catch (err: unknown) {
    const axiosError = err as { response?: { data?: { message?: string }; status?: number } }
    if (axiosError.response?.status === 400 && axiosError.response.data?.message) {
      error.value = axiosError.response.data.message
      turnstileRef.value?.reset()
      turnstileToken.value = ''
    } else if (!axiosError.response || (axiosError.response.status ?? 0) >= 500) {
      error.value = 'We could not send reset email right now. Please try again in a moment.'
    } else {
      success.value = true
    }
  } finally {
    isLoading.value = false
  }
}

// Check if field has error
function hasError(field: keyof ForgotPasswordFormErrors): boolean {
  return submitted.value && !!formErrors.value[field]
}

// Clear error when input changes
function clearError() {
  error.value = null
}

function handleTurnstileVerified(token: string) {
  turnstileToken.value = token
  turnstileError.value = null
}

function handleTurnstileExpired() {
  turnstileToken.value = ''
}

function handleTurnstileError() {
  turnstileToken.value = ''
  turnstileError.value = 'Verification failed to load. Please try again.'
}
</script>

<template>
  <AuthCard
    title="Forgot password"
    :lead="success ? 'Check your email' : 'Enter your email to reset your password'"
  >
    <template v-if="success">
      <Message severity="success" :closable="false" class="mb-4">
        If an account with that email exists, we've sent password reset instructions.
      </Message>
      <p class="auth-note mb-4">
        Please check your email inbox and spam folder. The link will expire in 24 hours.
      </p>
      <router-link to="/login">
        <Button label="Back to Sign In" class="auth-outline" />
      </router-link>
    </template>

    <template v-else>
      <Message v-if="error" severity="error" :closable="false" class="mb-4">
        {{ error }}
      </Message>

      <form class="auth-form" @submit.prevent="handleSubmit">
        <div class="auth-field">
          <label for="email">Email Address</label>
          <InputText
            id="email"
            v-model="email"
            type="email"
            placeholder="you@example.com"
            :invalid="hasError('email')"
            :disabled="isLoading"
            @input="clearError"
          />
          <small v-if="hasError('email')" class="text-red-500">{{ formErrors.email }}</small>
        </div>

        <div v-if="turnstileEnabled" class="auth-field">
          <CloudflareTurnstile
            ref="turnstileRef"
            :site-key="turnstileSiteKey"
            @verified="handleTurnstileVerified"
            @expired="handleTurnstileExpired"
            @error="handleTurnstileError"
          />
          <small v-if="turnstileError" class="text-red-500">{{ turnstileError }}</small>
        </div>

        <Button type="submit" label="Send Reset Link" class="auth-primary" :loading="isLoading" :disabled="isLoading" />

        <div class="auth-center">
          <router-link to="/login" class="auth-link">
            <i class="pi pi-arrow-left mr-1"></i>
            Back to Sign In
          </router-link>
        </div>
      </form>
    </template>
  </AuthCard>
</template>
