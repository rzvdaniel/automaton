<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import AuthCard from '../components/AuthCard.vue'
import Password from 'primevue/password'
import Button from 'primevue/button'
import Message from 'primevue/message'
import { authService } from '../services/auth.service'
import { useAuthStore } from '../stores/auth.store'

// Shown before anything else to a user whose administrator set their password.
const router = useRouter()
const authStore = useAuthStore()

const currentPassword = ref('')
const newPassword = ref('')
const confirmPassword = ref('')
const error = ref<string | null>(null)
const isLoading = ref(false)

function validate(): string | null {
  if (!currentPassword.value) return 'Enter the password you were given.'
  if (newPassword.value.length < 6) return 'The new password must be at least 6 characters.'
  if (!/(?=.*[a-z])(?=.*[A-Z])(?=.*\d)/.test(newPassword.value))
    return 'The new password must contain uppercase, lowercase, and a number.'
  if (newPassword.value === currentPassword.value) return 'Choose a password different from the one you were given.'
  if (newPassword.value !== confirmPassword.value) return 'The new passwords do not match.'
  return null
}

async function handleSubmit() {
  error.value = validate()
  if (error.value) return
  isLoading.value = true
  try {
    await authService.changePassword({ oldPassword: currentPassword.value, newPassword: newPassword.value })
    // Changing the password ends every session; sign in again with the new one.
    await authStore.logout()
    await router.replace({ path: '/login', query: { passwordChanged: 'true' } })
  } catch (err: unknown) {
    const response = (err as { response?: { status?: number; data?: { message?: string } } }).response
    error.value = response?.status === 400 && response.data?.message
      ? response.data.message
      : 'The password could not be changed. Check the password you were given and try again.'
  } finally {
    isLoading.value = false
  }
}

async function signOut() {
  await authStore.logout()
  await router.replace('/login')
}
</script>

<template>
  <AuthCard
    title="Choose your password"
    lead="An administrator set your current password. Choose your own before you continue."
  >
    <form class="auth-form" @submit.prevent="handleSubmit">
      <Message v-if="error" severity="error" :closable="false">{{ error }}</Message>
      <div class="auth-field">
        <label for="current-password">Password you were given</label>
        <Password
          v-model="currentPassword"
          input-id="current-password"
          :feedback="false"
          toggle-mask
          fluid
          autocomplete="current-password"
        />
      </div>
      <div class="auth-field">
        <label for="new-password">New password</label>
        <Password v-model="newPassword" input-id="new-password" toggle-mask fluid autocomplete="new-password" />
      </div>
      <div class="auth-field">
        <label for="confirm-password">Confirm new password</label>
        <Password
          v-model="confirmPassword"
          input-id="confirm-password"
          :feedback="false"
          toggle-mask
          fluid
          autocomplete="new-password"
        />
      </div>
      <Button type="submit" label="Change password" :loading="isLoading" class="auth-primary" />
      <Button type="button" label="Sign out" class="auth-text" @click="signOut" />
    </form>
  </AuthCard>
</template>
