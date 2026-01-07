<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import Card from 'primevue/card'
import InputText from 'primevue/inputtext'
import Password from 'primevue/password'
import Button from 'primevue/button'
import Checkbox from 'primevue/checkbox'
import Message from 'primevue/message'
import { useAuth } from '../composables/useAuth'
import type { RegisterFormErrors } from '../types/auth.types'

const { register, isLoading, error, clearError } = useAuth()
const router = useRouter()

// Form state
const name = ref('')
const email = ref('')
const password = ref('')
const confirmPassword = ref('')
const agreeTerms = ref(false)
const formErrors = ref<RegisterFormErrors>({})
const submitted = ref(false)

// Form validation
function validateForm(): boolean {
  formErrors.value = {}
  let isValid = true

  // Name validation
  if (!name.value) {
    formErrors.value.name = 'Full name is required'
    isValid = false
  } else if (name.value.length < 2) {
    formErrors.value.name = 'Name must be at least 2 characters'
    isValid = false
  }

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

  // Terms validation
  if (!agreeTerms.value) {
    formErrors.value.terms = 'You must agree to the Terms of Service'
    isValid = false
  }

  return isValid
}

// Handle form submission
async function handleSubmit() {
  submitted.value = true
  clearError()

  if (!validateForm()) {
    return
  }

  const success = await register({
    name: name.value,
    email: email.value,
    password: password.value,
  })

  if (success) {
    // Redirect to login with success message
    router.push({ path: '/login', query: { registered: 'true' } })
  }
}

// Check if field has error
function hasError(field: keyof RegisterFormErrors): boolean {
  return submitted.value && !!formErrors.value[field]
}
</script>

<template>
  <div class="register-view flex align-items-center justify-content-center min-h-screen">
    <Card class="register-card w-full" style="max-width: 400px">
      <template #header>
        <div class="text-center pt-4">
          <i class="pi pi-bolt text-4xl mb-3" style="color: var(--p-primary-500)"></i>
          <h1 class="text-2xl font-bold m-0">Create Account</h1>
          <p class="text-color-secondary mt-2">Get started with Automaton Studio</p>
        </div>
      </template>

      <template #content>
        <!-- Error message -->
        <Message v-if="error" severity="error" :closable="false" class="mb-4">
          {{ error }}
        </Message>

        <form class="flex flex-column gap-4" @submit.prevent="handleSubmit">
          <div class="flex flex-column gap-2">
            <label for="name" class="font-medium">Full Name</label>
            <InputText
              id="name"
              v-model="name"
              placeholder="Enter your name"
              class="w-full"
              :invalid="hasError('name')"
              :disabled="isLoading"
              @input="clearError"
            />
            <small v-if="hasError('name')" class="text-red-500">
              {{ formErrors.name }}
            </small>
          </div>

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
              placeholder="Create a password"
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
              placeholder="Confirm your password"
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

          <div class="flex flex-column gap-2">
            <div class="flex align-items-center gap-2">
              <Checkbox
                v-model="agreeTerms"
                input-id="terms"
                binary
                :invalid="hasError('terms')"
                :disabled="isLoading"
              />
              <label for="terms" class="text-sm">
                I agree to the
                <a href="#" class="text-primary no-underline hover:underline">Terms of Service</a>
                and
                <a href="#" class="text-primary no-underline hover:underline">Privacy Policy</a>
              </label>
            </div>
            <small v-if="hasError('terms')" class="text-red-500">
              {{ formErrors.terms }}
            </small>
          </div>

          <Button
            type="submit"
            label="Create Account"
            class="w-full"
            :loading="isLoading"
            :disabled="isLoading"
          />

          <div class="text-center">
            <span class="text-color-secondary text-sm">Already have an account? </span>
            <router-link to="/login" class="text-primary no-underline hover:underline text-sm">
              Sign in
            </router-link>
          </div>
        </form>
      </template>
    </Card>
  </div>
</template>

<style scoped>
.register-view {
  background-color: var(--p-surface-50);
}

:global(.app-dark) .register-view {
  background-color: var(--p-surface-950);
}

.register-card {
  margin: 1rem;
}
</style>
