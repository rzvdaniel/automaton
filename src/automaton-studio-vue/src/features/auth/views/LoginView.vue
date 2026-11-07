<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { installService } from '@/features/install/services/install.service'
import '../auth.css'
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
const serverHost = window.location.host

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
  } else if (route.query.passwordChanged === 'true') {
    successMessage.value = 'Password changed. Sign in with your new password.'
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
  <div class="signin auth-page">
    <section class="signin-side">
      <div class="auth-brand auth-brand--left"><span class="auth-mark" aria-hidden="true">&gt;_</span>Automaton</div>
      <div>
        <h1 class="signin-headline">All your AI-built apps, <span>on your server.</span></h1>
        <p class="signin-sub">Sign in to manage your apps, who can open them, backups and updates.</p>
      </div>
      <p class="signin-host">{{ serverHost }}</p>
    </section>

    <main class="signin-main">
      <div class="signin-form">
        <h2>Sign in</h2>
        <p class="auth-lead">New here? Ask your server's admin for an invite.</p>

        <Message v-if="successMessage" severity="success" :closable="false" class="mb-4">
          {{ successMessage }}
        </Message>

        <Message v-if="confirmationErrorMessage" severity="error" :closable="false" class="mb-4">
          {{ confirmationErrorMessage }}
        </Message>

        <Message v-if="error" severity="error" :closable="false" class="mb-4">
          {{ error }}
        </Message>

        <form class="auth-form" @submit.prevent="handleSubmit">
          <div class="auth-field">
            <label for="email">Email</label>
            <InputText
              id="email"
              v-model="email"
              type="email"
              autocomplete="username"
              placeholder="you@example.com"
              :invalid="hasError('email')"
              :disabled="isLoading"
              @input="clearError"
            />
            <small v-if="hasError('email')" class="text-red-500">{{ formErrors.email }}</small>
          </div>

          <div class="auth-field">
            <div class="auth-label-row">
              <label for="password">Password</label>
              <router-link to="/forgot-password" class="auth-link">Forgot password?</router-link>
            </div>
            <Password
              input-id="password"
              v-model="password"
              autocomplete="current-password"
              :feedback="false"
              toggle-mask
              input-class="w-full"
              :invalid="hasError('password')"
              :disabled="isLoading"
              @input="clearError"
            />
            <small v-if="hasError('password')" class="text-red-500">{{ formErrors.password }}</small>
          </div>

          <Button
            type="submit"
            label="Sign in"
            class="auth-primary"
            :loading="isLoading"
            :disabled="isLoading"
          />

          <Button
            v-if="gitHubEnabled"
            type="button"
            label="Continue with GitHub"
            icon="pi pi-github"
            class="auth-outline"
            :disabled="isLoading"
            @click="continueWithGitHub"
          />

          <div v-if="userSignUp" class="auth-center">
            <span class="auth-note">Don't have an account?&nbsp;</span>
            <router-link to="/register" class="auth-link">Sign up</router-link>
          </div>

          <p class="auth-note">
            Signing in from your AI agent? Approve the code it shows you after signing in.
          </p>
        </form>
      </div>
    </main>
  </div>
</template>
