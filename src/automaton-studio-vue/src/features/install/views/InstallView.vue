<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import Card from 'primevue/card'
import InputText from 'primevue/inputtext'
import Password from 'primevue/password'
import Button from 'primevue/button'
import Message from 'primevue/message'
import { installService } from '../services/install.service'
import type { InstallFormErrors } from '../types/install.types'

const router = useRouter()

// State
const isLoading = ref(false)
const isCheckingStatus = ref(true)
const installationComplete = ref(false)
const error = ref('')
const submitted = ref(false)

// Form state
const name = ref('')
const email = ref('')
const password = ref('')
const confirmPassword = ref('')
const formErrors = ref<InstallFormErrors>({})

// Check installation status on mount
onMounted(async () => {
  try {
    installationComplete.value = await installService.isInstallationComplete()
  } catch {
    // If we can't check status, assume installation is not complete
    installationComplete.value = false
  } finally {
    isCheckingStatus.value = false
  }
})

// Form validation
function validateForm(): boolean {
  formErrors.value = {}
  let isValid = true

  // Name validation
  if (!name.value) {
    formErrors.value.name = 'Name is required'
    isValid = false
  } else if (name.value.length > 256) {
    formErrors.value.name = 'Name must be 256 characters or less'
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
  } else if (password.value.length > 100) {
    formErrors.value.password = 'Password must be 100 characters or less'
    isValid = false
  }

  // Confirm password validation
  if (!confirmPassword.value) {
    formErrors.value.confirmPassword = 'Please confirm your password'
    isValid = false
  } else if (confirmPassword.value !== password.value) {
    formErrors.value.confirmPassword = 'Passwords do not match'
    isValid = false
  }

  return isValid
}

// Handle form submission
async function handleSubmit() {
  submitted.value = true
  error.value = ''

  if (!validateForm()) {
    return
  }

  isLoading.value = true

  try {
    await installService.install({
      name: name.value,
      email: email.value,
      password: password.value,
    })

    // Success - redirect to dashboard
    router.push('/')
  } catch {
    error.value = 'Installation failed. Please try again.'
  } finally {
    isLoading.value = false
  }
}

// Navigate to dashboard
function goToDashboard() {
  router.push('/')
}

// Check if field has error
function hasError(field: keyof InstallFormErrors): boolean {
  return submitted.value && !!formErrors.value[field]
}

// Clear error on input
function clearError() {
  error.value = ''
}
</script>

<template>
  <div class="install-view flex align-items-center justify-content-center min-h-screen">
    <!-- Loading state while checking status -->
    <div v-if="isCheckingStatus" class="text-center">
      <i class="pi pi-spin pi-spinner text-4xl text-primary"></i>
    </div>

    <!-- Installation Complete State -->
    <Card v-else-if="installationComplete" class="install-card w-full" style="max-width: 400px">
      <template #content>
        <div class="text-center py-4">
          <i class="pi pi-check-circle text-6xl mb-4" style="color: var(--p-green-500)"></i>
          <h1 class="text-2xl font-bold mb-3">Installation Complete</h1>
          <p class="text-color-secondary mb-4">
            Automaton has already been installed and configured.
          </p>
          <Button label="Go to Dashboard" class="w-full" @click="goToDashboard" />
        </div>
      </template>
    </Card>

    <!-- Installation Form -->
    <Card v-else class="install-card w-full" style="max-width: 400px">
      <template #header>
        <div class="text-center pt-4">
          <i class="pi pi-bolt text-4xl mb-3" style="color: var(--p-primary-500)"></i>
          <h1 class="text-2xl font-bold m-0">Welcome to Automaton Studio</h1>
          <p class="text-color-secondary mt-2">
            Let's set up your administrator account to get started
          </p>
        </div>
      </template>

      <template #content>
        <!-- Error message -->
        <Message v-if="error" severity="error" :closable="false" class="mb-4">
          {{ error }}
        </Message>

        <form class="flex flex-column gap-4" @submit.prevent="handleSubmit">
          <!-- Name field -->
          <div class="flex flex-column gap-2">
            <label for="name" class="font-medium">Name</label>
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

          <!-- Email field -->
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

          <!-- Password field -->
          <div class="flex flex-column gap-2">
            <label for="password" class="font-medium">Password</label>
            <Password
              id="password"
              v-model="password"
              placeholder="Enter your password"
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
              Must be at least 6 characters long
            </small>
          </div>

          <!-- Confirm Password field -->
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

          <!-- Submit button -->
          <Button
            type="submit"
            label="Complete Setup"
            class="w-full mt-2"
            :loading="isLoading"
            :disabled="isLoading"
          />
        </form>
      </template>
    </Card>
  </div>
</template>

<style scoped>
.install-view {
  background-color: var(--p-surface-50);
}

:global(.app-dark) .install-view {
  background-color: var(--p-surface-950);
}

.install-card {
  margin: 1rem;
}
</style>
