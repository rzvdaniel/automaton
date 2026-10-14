<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import Card from 'primevue/card'
import InputText from 'primevue/inputtext'
import Password from 'primevue/password'
import Button from 'primevue/button'
import Checkbox from 'primevue/checkbox'
import Message from 'primevue/message'
import Tag from 'primevue/tag'
import CloudflareTurnstile from '@/shared/components/CloudflareTurnstile.vue'
import { useAuth } from '../composables/useAuth'
import { authService } from '../services/auth.service'
import type { RegisterFormErrors } from '../types/auth.types'

const { register, isLoading, error, clearError } = useAuth()
const router = useRouter()

const registrationEnabled = ref(false)
const registrationStatusMessage = ref('Checking registration availability...')
onMounted(async () => {
  try {
    const status = await authService.registrationStatus()
    registrationEnabled.value = status.enabled
    registrationStatusMessage.value = status.message || ''
  } catch {
    registrationStatusMessage.value = 'Unable to check registration availability. Please try again later.'
  }
})

// Form state
const name = ref('')
const email = ref('')
const password = ref('')
const confirmPassword = ref('')
const publicUsername = ref('')
const agreeTerms = ref(false)
const formErrors = ref<RegisterFormErrors>({})
const submitted = ref(false)
const checkingUsername = ref(false)
const usernameAvailable = ref<boolean | null>(null)
const turnstileToken = ref('')
const turnstileError = ref<string | null>(null)
const turnstileRef = ref<InstanceType<typeof CloudflareTurnstile> | null>(null)
const turnstileSiteKey = import.meta.env.VITE_TURNSTILE_SITE_KEY || ''
const turnstileEnabled =
  import.meta.env.VITE_TURNSTILE_ENABLED === 'true' && !!turnstileSiteKey

// Auto-generate public username from display name
function generatePublicUsername(displayName: string): string {
  // Convert to lowercase
  let slug = displayName.toLowerCase()
  // Remove accents and special chars
  slug = slug.replace(/[^\w\s-]/g, '')
  // Replace spaces with hyphens
  slug = slug.replace(/\s+/g, '-')
  // Remove multiple hyphens
  slug = slug.replace(/-+/g, '-')
  // Trim hyphens
  slug = slug.replace(/^-+|-+$/g, '')
  // Limit length
  if (slug.length > 32) slug = slug.substring(0, 32)
  return slug || 'user'
}

// Check username availability
async function checkUsernameAvailability() {
  if (!publicUsername.value || publicUsername.value.length < 3) {
    usernameAvailable.value = null
    return
  }

  checkingUsername.value = true
  try {
    usernameAvailable.value = await authService.checkPublicUsernameAvailable(publicUsername.value)
  } catch {
    usernameAvailable.value = null
  } finally {
    checkingUsername.value = false
  }
}

// Computed for username availability status
const usernameStatus = computed(() => {
  if (!publicUsername.value) return null
  if (checkingUsername.value) return 'checking'
  if (usernameAvailable.value === null) return null
  return usernameAvailable.value ? 'available' : 'taken'
})

// Handle name input - auto-generate username
function handleNameInput() {
  if (!submitted.value || !publicUsername.value) {
    // Auto-generate if not yet submitted or user hasn't set custom username
    const suggested = generatePublicUsername(name.value)
    publicUsername.value = suggested
    checkUsernameAvailability()
  }
}

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

  // Public username validation
  if (!publicUsername.value) {
    formErrors.value.publicUsername = 'Public username is required'
    isValid = false
  } else if (publicUsername.value.length < 3 || publicUsername.value.length > 32) {
    formErrors.value.publicUsername = 'Public username must be 3-32 characters'
    isValid = false
  } else if (!/^[a-zA-Z0-9_\-]+$/.test(publicUsername.value)) {
    formErrors.value.publicUsername = 'Public username can only contain letters, numbers, hyphens, and underscores'
    isValid = false
  } else if (usernameAvailable.value === false) {
    formErrors.value.publicUsername = 'This public username is already taken'
    isValid = false
  }

  // Terms validation
  if (!agreeTerms.value) {
    formErrors.value.terms = 'You must agree to the Terms of Service'
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
  if (!registrationEnabled.value) return
  submitted.value = true
  clearError()
  turnstileError.value = null

  // Ensure availability has been checked before validating
  if (publicUsername.value && publicUsername.value.length >= 3) {
    await checkUsernameAvailability()
  }

  if (!validateForm()) {
    return
  }

  const success = await register({
    name: name.value,
    email: email.value,
    password: password.value,
    publicUsername: publicUsername.value,
    turnstileToken: turnstileToken.value || undefined,
  })

  if (success) {
    // Redirect to login with success message
    router.push({ path: '/login', query: { registered: 'true' } })
  }

  turnstileRef.value?.reset()
  turnstileToken.value = ''
}

// Check if field has error
function hasError(field: keyof RegisterFormErrors): boolean {
  return submitted.value && !!formErrors.value[field]
}

function continueWithGitHub() {
  authService.startGitHubLogin('/')
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
  <div class="register-view flex align-items-center justify-content-center min-h-screen">
    <Card class="register-card w-full" style="max-width: 400px">
      <template #header>
        <div class="auth-card-header text-center pt-4">
          <img src="/logo.svg" alt="Automaton logo" class="auth-logo mb-3" />
          <h1 class="text-2xl font-bold m-0">Create Account</h1>
          <p class="text-color-secondary mt-2">Get started with Automaton Studio</p>
        </div>
      </template>

      <template #content>
        <Message v-if="!registrationEnabled" severity="info" :closable="false" class="mb-4">
          {{ registrationStatusMessage }}
        </Message>
        <div v-if="!registrationEnabled" class="text-center">
          <router-link to="/login" class="text-primary no-underline hover:underline">Sign in</router-link>
        </div>
        <!-- Error message -->
        <Message v-if="error" severity="error" :closable="false" class="mb-4">
          {{ error }}
        </Message>

        <form v-if="registrationEnabled" class="flex flex-column gap-4" @submit.prevent="handleSubmit">
          <div class="flex flex-column gap-2">
            <label for="name" class="font-medium">Full Name</label>
            <InputText
              id="name"
              v-model="name"
              placeholder="Enter your name"
              class="w-full"
              :invalid="hasError('name')"
              :disabled="isLoading"
              @input="handleNameInput"
              @blur="clearError"
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
            <div class="flex align-items-center justify-content-between">
              <label for="publicUsername" class="font-medium">Public Username</label>
              <Tag 
                v-if="usernameStatus === 'available'" 
                severity="success" 
                value="Available" 
                class="text-xs"
              />
              <Tag 
                v-else-if="usernameStatus === 'taken'" 
                severity="danger" 
                value="Taken" 
                class="text-xs"
              />
              <Tag 
                v-else-if="usernameStatus === 'checking'" 
                severity="info" 
                value="Checking..." 
                class="text-xs"
              />
            </div>
            <InputText
              id="publicUsername"
              v-model="publicUsername"
              placeholder="Your public profile URL"
              class="w-full"
              :invalid="hasError('publicUsername')"
              :disabled="isLoading"
              @input="checkUsernameAvailability"
              @blur="checkUsernameAvailability"
            />
            <small class="text-color-secondary text-xs">
              3-32 characters. Letters, numbers, hyphens, and underscores only.
            </small>
            <small v-if="hasError('publicUsername')" class="text-red-500">
              {{ formErrors.publicUsername }}
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
                <a href="/terms" target="_blank" rel="noopener noreferrer" class="text-primary no-underline hover:underline">Terms of Service</a>
                and
                <a href="/privacy" target="_blank" rel="noopener noreferrer" class="text-primary no-underline hover:underline">Privacy Policy</a>
                and understand that Automaton is an experimental preview platform that may experience instability, service interruptions, or data loss.
              </label>
            </div>
            <small v-if="hasError('terms')" class="text-red-500">
              {{ formErrors.terms }}
            </small>
          </div>

          <div v-if="turnstileEnabled" class="flex flex-column gap-2">
            <CloudflareTurnstile
              ref="turnstileRef"
              :site-key="turnstileSiteKey"
              @verified="handleTurnstileVerified"
              @expired="handleTurnstileExpired"
              @error="handleTurnstileError"
            />
            <small v-if="turnstileError" class="text-red-500">
              {{ turnstileError }}
            </small>
          </div>

          <Button
            type="submit"
            label="Create Account"
            class="w-full"
            :loading="isLoading"
            :disabled="isLoading"
          />

          <Button
            type="button"
            label="Continue with GitHub"
            icon="pi pi-github"
            severity="secondary"
            outlined
            class="w-full"
            :disabled="isLoading"
            @click="continueWithGitHub"
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
</style>
