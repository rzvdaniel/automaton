<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import Card from 'primevue/card'
import Password from 'primevue/password'
import Button from 'primevue/button'
import Message from 'primevue/message'
import { authService } from '../services/auth.service'
import type { ResetPasswordFormErrors } from '../types/auth.types'

const route = useRoute()
const router = useRouter()

// Get email and code from URL query params
const email = ref('')
const code = ref('')

// Determine if this is creating a new password or resetting
const isCreatingPassword = ref(false)

// Form state
const password = ref('')
const confirmPassword = ref('')
const formErrors = ref<ResetPasswordFormErrors>({})
const submitted = ref(false)
const isLoading = ref(false)
const error = ref<string | null>(null)
const success = ref(false)
const invalidLink = ref(false)

onMounted(() => {
  // Extract email and code from query params
  const queryEmail = route.query.email
  const queryCode = route.query.code
  const queryCreate = route.query.create // Optional flag to indicate password creation

  if (typeof queryEmail === 'string' && typeof queryCode === 'string') {
    email.value = queryEmail
    code.value = queryCode
    isCreatingPassword.value = queryCreate === 'true' // Check if creating new password
  } else {
    invalidLink.value = true
  }
})

// Form validation
function validateForm(): boolean {
  formErrors.value = {}
  let isValid = true

  // Password validation
  if (!password.value) {
    formErrors.value.password = 'Password is required'
    isValid = false
  } else if (password.value.length < 6) {
    formErrors.value.password = 'Password must be at least 6 characters'
    isValid = false
  } else if (!/(?=.*[a-z])(?=.*[A-Z])(?=.*\d)/.test(password.value)) {
    formErrors.value.password = 'Password must contain uppercase, lowercase, and number'
    isValid = false
  }

  // Confirm password validation
  if (!confirmPassword.value) {
    formErrors.value.confirmPassword = 'Please confirm your password'
    isValid = false
  } else if (password.value !== confirmPassword.value) {
    formErrors.value.confirmPassword = 'Passwords do not match'
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
    await authService.resetPassword({
      email: email.value,
      password: password.value,
      code: code.value,
    })
    success.value = true
  } catch (err: unknown) {
    const axiosError = err as { response?: { data?: { message?: string }; status?: number } }
    if (axiosError.response?.status === 400) {
      error.value = 'Invalid or expired reset link. Please request a new one.'
    } else if (axiosError.response?.data?.message) {
      error.value = axiosError.response.data.message
    } else {
      error.value = 'Failed to reset password. Please try again.'
    }
  } finally {
    isLoading.value = false
  }
}

// Check if field has error
function hasError(field: keyof ResetPasswordFormErrors): boolean {
  return submitted.value && !!formErrors.value[field]
}

// Navigate to login
function goToLogin() {
  router.push('/login')
}

// Clear error when input changes
function clearError() {
  error.value = null
}
</script>

<template>
  <div class="reset-password-view flex align-items-center justify-content-center min-h-screen">
    <Card class="reset-password-card w-full" style="max-width: 400px">
      <template #header>
        <div class="text-center pt-4">
          <i
            :class="success ? 'pi pi-check-circle' : 'pi pi-lock'"
            class="text-4xl mb-3"
            :style="{ color: success ? 'var(--p-green-500)' : 'var(--p-primary-500)' }"
          ></i>
          <h1 class="text-2xl font-bold m-0">
            {{ success ? 'Password Set Successfully' : isCreatingPassword ? 'Create Your Password' : 'Set New Password' }}
          </h1>
          <p class="text-color-secondary mt-2">
            {{ success ? 'Your password has been updated' : isCreatingPassword ? 'Set up a password for your account' : 'Create a new password for your account' }}
          </p>
        </div>
      </template>

      <template #content>
        <!-- Invalid link -->
        <template v-if="invalidLink">
          <Message severity="error" :closable="false" class="mb-4">
            Invalid password reset link. The link may be expired or malformed.
          </Message>
          <router-link to="/forgot-password" class="w-full block mb-3">
            <Button label="Request New Link" class="w-full" />
          </router-link>
          <router-link to="/login" class="w-full block">
            <Button label="Back to Sign In" class="w-full" outlined />
          </router-link>
        </template>

        <!-- Success message -->
        <template v-else-if="success">
          <Message severity="success" :closable="false" class="mb-4">
            {{ isCreatingPassword ? 'Your password has been successfully created.' : 'Your password has been successfully reset.' }}
          </Message>
          <Button label="Sign In" class="w-full" @click="goToLogin" />
        </template>

        <!-- Form -->
        <template v-else>
          <!-- Error message -->
          <Message v-if="error" severity="error" :closable="false" class="mb-4">
            {{ error }}
          </Message>

          <form class="flex flex-column gap-4" @submit.prevent="handleSubmit">
            <div class="flex flex-column gap-2">
              <label for="password" class="font-medium">New Password</label>
              <Password
                id="password"
                v-model="password"
                placeholder="Enter new password"
                toggle-mask
                class="w-full"
                input-class="w-full"
                :invalid="hasError('password')"
                :disabled="isLoading"
                @input="clearError"
              />
              <small v-if="hasError('password')" class="text-red-500">
                {{ formErrors.password }}
              </small>
              <small v-else class="text-color-secondary">
                Must contain uppercase, lowercase, and number
              </small>
            </div>

            <div class="flex flex-column gap-2">
              <label for="confirmPassword" class="font-medium">Confirm Password</label>
              <Password
                id="confirmPassword"
                v-model="confirmPassword"
                placeholder="Confirm new password"
                :feedback="false"
                toggle-mask
                class="w-full"
                input-class="w-full"
                :invalid="hasError('confirmPassword')"
                :disabled="isLoading"
                @input="clearError"
              />
              <small v-if="hasError('confirmPassword')" class="text-red-500">
                {{ formErrors.confirmPassword }}
              </small>
            </div>

            <Button
              type="submit"
              :label="isCreatingPassword ? 'Create Password' : 'Reset Password'"
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
.reset-password-view {
  background-color: var(--p-surface-50);
}

:global(.app-dark) .reset-password-view {
  background-color: var(--p-surface-950);
}

.reset-password-card {
  margin: 1rem;
}
</style>
