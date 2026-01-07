<script setup lang="ts">
import { ref } from 'vue'
import Card from 'primevue/card'
import InputText from 'primevue/inputtext'
import Button from 'primevue/button'
import Message from 'primevue/message'
import { authService } from '../services/auth.service'
import type { ForgotPasswordFormErrors } from '../types/auth.types'

// Form state
const email = ref('')
const formErrors = ref<ForgotPasswordFormErrors>({})
const submitted = ref(false)
const isLoading = ref(false)
const error = ref<string | null>(null)
const success = ref(false)

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

  return isValid
}

// Handle form submission
async function handleSubmit() {
  submitted.value = true
  error.value = null

  if (!validateForm()) {
    return
  }

  isLoading.value = true

  try {
    await authService.forgotPassword({ email: email.value })
    success.value = true
  } catch (err: unknown) {
    // Even if the email doesn't exist, we show success for security reasons
    // The backend should handle this silently
    success.value = true
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
</script>

<template>
  <div class="forgot-password-view flex align-items-center justify-content-center min-h-screen">
    <Card class="forgot-password-card w-full" style="max-width: 400px">
      <template #header>
        <div class="text-center pt-4">
          <i class="pi pi-lock text-4xl mb-3" style="color: var(--p-primary-500)"></i>
          <h1 class="text-2xl font-bold m-0">Forgot Password</h1>
          <p class="text-color-secondary mt-2">
            {{ success ? 'Check your email' : 'Enter your email to reset your password' }}
          </p>
        </div>
      </template>

      <template #content>
        <!-- Success message -->
        <template v-if="success">
          <Message severity="success" :closable="false" class="mb-4">
            If an account with that email exists, we've sent password reset instructions.
          </Message>
          <p class="text-color-secondary text-sm mb-4">
            Please check your email inbox and spam folder. The link will expire in 24 hours.
          </p>
          <router-link to="/login" class="w-full">
            <Button label="Back to Sign In" class="w-full" outlined />
          </router-link>
        </template>

        <!-- Form -->
        <template v-else>
          <!-- Error message -->
          <Message v-if="error" severity="error" :closable="false" class="mb-4">
            {{ error }}
          </Message>

          <form class="flex flex-column gap-4" @submit.prevent="handleSubmit">
            <div class="flex flex-column gap-2">
              <label for="email" class="font-medium">Email Address</label>
              <InputText
                id="email"
                v-model="email"
                type="email"
                placeholder="Enter your email"
                class="w-full"
                :invalid="hasError('email')"
                :disabled="isLoading"
                @input="clearError"
              />
              <small v-if="hasError('email')" class="text-red-500">
                {{ formErrors.email }}
              </small>
            </div>

            <Button
              type="submit"
              label="Send Reset Link"
              class="w-full"
              :loading="isLoading"
              :disabled="isLoading"
            />

            <div class="text-center">
              <router-link to="/login" class="text-primary no-underline hover:underline text-sm">
                <i class="pi pi-arrow-left mr-1"></i>
                Back to Sign In
              </router-link>
            </div>
          </form>
        </template>
      </template>
    </Card>
  </div>
</template>

<style scoped>
.forgot-password-view {
  background-color: var(--p-surface-50);
}

:global(.app-dark) .forgot-password-view {
  background-color: var(--p-surface-950);
}

.forgot-password-card {
  margin: 1rem;
}
</style>
