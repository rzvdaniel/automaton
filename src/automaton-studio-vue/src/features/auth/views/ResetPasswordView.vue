<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import AuthCard from '../components/AuthCard.vue'
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
  <AuthCard
    :title="success ? 'Password set successfully' : isCreatingPassword ? 'Create your password' : 'Set new password'"
    :lead="success ? 'Your password has been updated' : isCreatingPassword ? 'Set up a password for your account' : 'Create a new password for your account'"
  >
    <template v-if="invalidLink">
      <Message severity="error" :closable="false" class="mb-4">
        Invalid password reset link. The link may be expired or malformed.
      </Message>
      <div class="auth-form">
        <router-link to="/forgot-password"><Button label="Request New Link" class="auth-primary" /></router-link>
        <router-link to="/login"><Button label="Back to Sign In" class="auth-outline" /></router-link>
      </div>
    </template>

    <template v-else-if="success">
      <Message severity="success" :closable="false" class="mb-4">
        {{ isCreatingPassword ? 'Your password has been successfully created.' : 'Your password has been successfully reset.' }}
      </Message>
      <Button label="Sign In" class="auth-primary" @click="goToLogin" />
    </template>

    <template v-else>
      <Message v-if="error" severity="error" :closable="false" class="mb-4">
        {{ error }}
      </Message>

      <form class="auth-form" @submit.prevent="handleSubmit">
        <div class="auth-field">
          <label for="password">New Password</label>
          <Password
            id="password"
            v-model="password"
            placeholder="Enter new password"
            toggle-mask
            autocomplete="new-password"
            input-class="w-full"
            :invalid="hasError('password')"
            :disabled="isLoading"
            @input="clearError"
          />
          <small v-if="hasError('password')" class="text-red-500">{{ formErrors.password }}</small>
          <small v-else class="auth-note">Must contain uppercase, lowercase, and number</small>
        </div>

        <div class="auth-field">
          <label for="confirmPassword">Confirm Password</label>
          <Password
            id="confirmPassword"
            v-model="confirmPassword"
            placeholder="Confirm new password"
            :feedback="false"
            toggle-mask
            autocomplete="new-password"
            input-class="w-full"
            :invalid="hasError('confirmPassword')"
            :disabled="isLoading"
            @input="clearError"
          />
          <small v-if="hasError('confirmPassword')" class="text-red-500">{{ formErrors.confirmPassword }}</small>
        </div>

        <Button
          type="submit"
          :label="isCreatingPassword ? 'Create Password' : 'Reset Password'"
          class="auth-primary"
          :loading="isLoading"
          :disabled="isLoading"
        />

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
