<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { installService } from '@/features/install/services/install.service'
import Card from 'primevue/card'
import InputText from 'primevue/inputtext'
import Password from 'primevue/password'
import Button from 'primevue/button'
import Message from 'primevue/message'
import { useAuth } from '../composables/useAuth'
import { authService } from '../services/auth.service'
import type { LoginFormErrors } from '../types/auth.types'

const { login, isLoading, error, clearError } = useAuth()
const route = useRoute()
const router = useRouter()
const gitHubEnabled = ref(false)
const userSignUp = ref(false)

// Form state
const email = ref('')
const password = ref('')
const formErrors = ref<LoginFormErrors>({})
const submitted = ref(false)

// Success message (e.g., after registration)
const successMessage = ref('')
const confirmationErrorMessage = ref('')

// Get redirect URL from query params
const redirectTo = computed(() => {
  const redirect = route.query.redirect
  return typeof redirect === 'string' ? redirect : '/'
})

onMounted(async () => {
  try {
    const status = await installService.getStatus()
    if (!status.installationComplete) { await router.replace('/install'); return }
    gitHubEnabled.value = status.gitHubEnabled
    userSignUp.value = status.userSignUp
  } catch {
    // Keep password login available if the optional installation-status check fails.
  }
  // Check for success message from registration
  if (route.query.setup === 'complete') {
    successMessage.value = 'Administrator created. Sign in to continue.'
  } else if (route.query.registered === 'true') {
    successMessage.value = 'Registration successful! Please sign in with your credentials.'
  } else if (route.query.emailConfirmation === 'success') {
    successMessage.value = 'Email confirmed. Sign in to continue to Studio with your account fully activated.'
  } else if (route.query.emailConfirmation === 'failed') {
    confirmationErrorMessage.value = 'Email confirmation failed or the link is no longer valid. Please sign in and resend the confirmation email.'
  }
  clearError()
})

// Form validation
function validateForm(): boolean {
  formErrors.value = {}
  let isValid = true

  // Email validation
  if (!email.value) {
    formErrors.value.email = 'Email is required'
    isValid = false
  } else if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email.value)) {
    formErrors.value.email = 'Please enter a valid email address'
    isValid = false
  }

  // Password validation
  if (!password.value) {
    formErrors.value.password = 'Password is required'
    isValid = false
  } else if (password.value.length < 6) {
    formErrors.value.password = 'Password must be at least 6 characters'
    isValid = false
  }

  return isValid
}

// Handle form submission
async function handleSubmit() {
  submitted.value = true
  clearError()
  successMessage.value = ''

  if (!validateForm()) {
    return
  }

  await login(
    {
      email: email.value,
      password: password.value,
    },
    redirectTo.value,
  )
}

// Check if field has error
function hasError(field: keyof LoginFormErrors): boolean {
  return submitted.value && !!formErrors.value[field]
}

function continueWithGitHub() {
  authService.startGitHubLogin(redirectTo.value)
}
</script>

<template>
  <div class="login-view flex align-items-center justify-content-center min-h-screen">
    <Card class="login-card w-full" style="max-width: 400px">
      <template #header>
        <div class="auth-card-header text-center pt-4">
          <img src="/logo.svg" alt="Automaton logo" class="auth-logo mb-3" />
          <h1 class="text-2xl font-bold m-0">Automaton Studio</h1>
          <p class="text-color-secondary mt-2">Sign in to your account</p>
        </div>
      </template>

      <template #content>
        <!-- Success message -->
        <Message v-if="successMessage" severity="success" :closable="false" class="mb-4">
          {{ successMessage }}
        </Message>

        <Message v-if="confirmationErrorMessage" severity="error" :closable="false" class="mb-4">
          {{ confirmationErrorMessage }}
        </Message>

        <!-- Error message -->
        <Message v-if="error" severity="error" :closable="false" class="mb-4">
          {{ error }}
        </Message>

        <form class="flex flex-column gap-4" @submit.prevent="handleSubmit">
          <div class="flex flex-column gap-2">
            <label for="email" class="font-medium">Email</label>
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

          <div class="flex flex-column gap-2">
            <label for="password" class="font-medium">Password</label>
            <Password
              id="password"
              v-model="password"
              placeholder="Enter your password"
              :feedback="false"
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
          </div>

          <div class="flex align-items-center justify-content-end">
            <router-link to="/forgot-password" class="text-sm text-primary no-underline hover:underline">
              Forgot password?
            </router-link>
          </div>

          <Button
            type="submit"
            label="Sign In"
            class="w-full"
            :loading="isLoading"
            :disabled="isLoading"
          />

          <Button
            type="button"
            v-if="gitHubEnabled"
            label="Continue with GitHub"
            icon="pi pi-github"
            severity="secondary"
            outlined
            class="w-full"
            :disabled="isLoading"
            @click="continueWithGitHub"
          />

          <div v-if="userSignUp" class="text-center">
            <span class="text-color-secondary text-sm">Don't have an account? </span>
            <router-link to="/register" class="text-primary no-underline hover:underline text-sm">
              Sign up
            </router-link>
          </div>

          <p class="login-preview-note text-center">
            Automaton is an experimental developer preview platform. Features and infrastructure
            may change without notice.
          </p>
        </form>
      </template>
    </Card>
  </div>
</template>

<style scoped>
.login-view {
  background-color: var(--p-surface-50);
}

:global(.app-dark) .login-view {
  background-color: var(--p-surface-950);
}

.login-card {
  margin: 1rem;
}

.auth-card-header {
  display: flex;
  flex-direction: column;
  align-items: center;
}

.auth-logo {
  width: 4rem;
  height: 4rem;
  display: block;
}

.login-preview-note {
  margin: 0;
  color: var(--p-text-muted-color);
  font-size: 0.8rem;
  line-height: 1.55;
}
</style>
