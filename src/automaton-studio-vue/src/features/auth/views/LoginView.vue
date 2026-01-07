<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import Card from 'primevue/card'
import InputText from 'primevue/inputtext'
import Password from 'primevue/password'
import Button from 'primevue/button'
import Checkbox from 'primevue/checkbox'
import Message from 'primevue/message'
import { useAuth } from '../composables/useAuth'
import type { LoginFormErrors } from '../types/auth.types'

const { login, isLoading, error, clearError } = useAuth()
const route = useRoute()

// Form state
const email = ref('')
const password = ref('')
const rememberMe = ref(false)
const formErrors = ref<LoginFormErrors>({})
const submitted = ref(false)

// Success message (e.g., after registration)
const successMessage = ref('')

// Get redirect URL from query params
const redirectTo = computed(() => {
  const redirect = route.query.redirect
  return typeof redirect === 'string' ? redirect : '/'
})

onMounted(() => {
  // Check for success message from registration
  if (route.query.registered === 'true') {
    successMessage.value = 'Registration successful! Please sign in with your credentials.'
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
</script>

<template>
  <div class="login-view flex align-items-center justify-content-center min-h-screen">
    <Card class="login-card w-full" style="max-width: 400px">
      <template #header>
        <div class="text-center pt-4">
          <i class="pi pi-bolt text-4xl mb-3" style="color: var(--p-primary-500)"></i>
          <h1 class="text-2xl font-bold m-0">Automaton Studio</h1>
          <p class="text-color-secondary mt-2">Sign in to your account</p>
        </div>
      </template>

      <template #content>
        <!-- Success message -->
        <Message v-if="successMessage" severity="success" :closable="false" class="mb-4">
          {{ successMessage }}
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

          <div class="flex align-items-center justify-content-between">
            <div class="flex align-items-center gap-2">
              <Checkbox v-model="rememberMe" input-id="remember" binary :disabled="isLoading" />
              <label for="remember" class="text-sm">Remember me</label>
            </div>
            <a href="#" class="text-sm text-primary no-underline hover:underline">
              Forgot password?
            </a>
          </div>

          <Button
            type="submit"
            label="Sign In"
            class="w-full"
            :loading="isLoading"
            :disabled="isLoading"
          />

          <div class="text-center">
            <span class="text-color-secondary text-sm">Don't have an account? </span>
            <router-link to="/register" class="text-primary no-underline hover:underline text-sm">
              Sign up
            </router-link>
          </div>
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
</style>
