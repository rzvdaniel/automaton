<script setup lang="ts">
import { ref, onMounted, watch, computed } from 'vue'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import InputNumber from 'primevue/inputnumber'
import Password from 'primevue/password'
import SelectButton from 'primevue/selectbutton'
import Dialog from 'primevue/dialog'
import Message from 'primevue/message'
import { useToast } from 'primevue/usetoast'
import { useUserSettings } from '@/shared/composables/useUserSettings'
import { useAuthStore } from '@/features/auth/stores/auth.store'
import { authService } from '@/features/auth/services/auth.service'
import { getPublicStoreUrl } from '@/shared/utils/public-store-url'
import type { ChangePasswordFormErrors } from '@/features/auth/types/auth.types'

const { theme: themePreference, fontSize, isLoading: settingsLoading, isSaving: settingsSaving, setTheme, setFontSize } = useUserSettings()
const authStore = useAuthStore()
const toast = useToast()

const passwordRule = 'At least 8 characters, with an uppercase letter, a lowercase letter and a number.'

const displayName = ref('')
const email = ref('')
const publicUsername = ref('')
const originalPublicUsername = ref('')
const checkingUsername = ref(false)
const usernameAvailable = ref<boolean | null>(null)
const accountSaving = ref(false)
const accountError = ref<string | null>(null)
const editorFontSize = ref(14)

onMounted(() => {
  if (authStore.user) {
    displayName.value = authStore.user.name || ''
    email.value = authStore.user.email || ''
    if (authStore.user.publicUsername) {
      publicUsername.value = authStore.user.publicUsername
      originalPublicUsername.value = authStore.user.publicUsername
    }
  }
  editorFontSize.value = fontSize.value
})
watch(fontSize, (size) => { editorFontSize.value = size })

const usernameChanged = computed(() => publicUsername.value !== originalPublicUsername.value)
const usernameHint = computed(() => {
  if (!usernameChanged.value || !publicUsername.value) return ''
  if (publicUsername.value.length < 3) return 'At least 3 characters.'
  if (checkingUsername.value) return 'Checking…'
  if (usernameAvailable.value === false) return 'Taken.'
  return usernameAvailable.value ? 'Available.' : ''
})

async function checkUsernameAvailability() {
  if (!usernameChanged.value || publicUsername.value.length < 3) {
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

async function saveAccountSettings() {
  accountError.value = null
  if (!usernameChanged.value) return
  if (publicUsername.value.length < 3 || publicUsername.value.length > 32) {
    accountError.value = 'Public username must be 3-32 characters'
    return
  }
  if (!/^[a-zA-Z0-9_-]+$/.test(publicUsername.value)) {
    accountError.value = 'Public username can only contain letters, numbers, hyphens, and underscores'
    return
  }
  if (!usernameAvailable.value) {
    accountError.value = 'This public username is already taken'
    return
  }
  accountSaving.value = true
  try {
    await authService.updatePublicUsername(publicUsername.value)
    authStore.setPublicUsername(publicUsername.value)
    await authStore.fetchUserProfile()
    originalPublicUsername.value = publicUsername.value
    toast.add({ severity: 'success', summary: 'Saved', detail: 'Your profile has been updated.', life: 3000 })
  } catch (err: any) {
    console.error('Failed to save account settings:', err)
    let message = 'Failed to save account settings. Please try again.'
    const response = err?.response?.data
    if (response?.errors) {
      const first = response.errors.NewPublicUsername?.[0] ?? Object.values<string[]>(response.errors)[0]?.[0]
      if (first) message = first
    } else if (response?.message) {
      message = response.message
    }
    accountError.value = message
    toast.add({ severity: 'error', summary: 'Save Failed', detail: message, life: 5000 })
  } finally {
    accountSaving.value = false
  }
}

function goToPublicProfile() {
  if (publicUsername.value) window.open(getPublicStoreUrl(publicUsername.value), '_blank', 'noopener')
}

const themeOptions = [
  { label: 'Light', value: 'light', icon: 'pi pi-sun' },
  { label: 'Dark', value: 'dark', icon: 'pi pi-moon' },
  { label: 'System', value: 'system', icon: 'pi pi-desktop' },
]

async function handleThemeChange(newTheme: string | null) {
  if (newTheme) await setTheme(newTheme as 'light' | 'dark' | 'system')
}

/** The editor font size saves as soon as it holds a valid, changed value. */
async function handleFontSize(value: number | null) {
  editorFontSize.value = value ?? editorFontSize.value
  if (value === null || value < 10 || value > 24 || value === fontSize.value) return
  try {
    await setFontSize(value)
  } catch (err) {
    console.error('Failed to save editor settings:', err)
    toast.add({ severity: 'error', summary: 'Save Failed', detail: 'Could not save editor settings.', life: 5000 })
  }
}

const showPasswordDialog = ref(false)
const oldPassword = ref('')
const newPassword = ref('')
const confirmPassword = ref('')
const passwordSaving = ref(false)
const passwordError = ref<ChangePasswordFormErrors | null>(null)
const passwordSuccess = ref(false)

function openPasswordDialog() {
  showPasswordDialog.value = true
  oldPassword.value = ''
  newPassword.value = ''
  confirmPassword.value = ''
  passwordError.value = null
  passwordSuccess.value = false
}

/** Same rule as sign-up and password reset. */
function meetsPasswordRule(value: string): boolean {
  return value.length >= 8 && /[a-z]/.test(value) && /[A-Z]/.test(value) && /[0-9]/.test(value)
}

async function handlePasswordSubmit() {
  passwordError.value = null
  passwordSuccess.value = false
  const errors: ChangePasswordFormErrors = {}
  if (!oldPassword.value) errors.oldPassword = 'Current password is required'
  if (!meetsPasswordRule(newPassword.value)) errors.newPassword = passwordRule
  if (newPassword.value !== confirmPassword.value) errors.confirmPassword = 'Passwords do not match'
  if (Object.keys(errors).length > 0) {
    passwordError.value = errors
    return
  }
  passwordSaving.value = true
  try {
    await authService.changePassword({ oldPassword: oldPassword.value, newPassword: newPassword.value })
    passwordSuccess.value = true
    toast.add({ severity: 'success', summary: 'Password Changed', detail: 'Your password has been updated successfully.', life: 3000 })
    setTimeout(() => { showPasswordDialog.value = false }, 1500)
  } catch (err: any) {
    const response = err?.response?.data
    passwordError.value = response?.errors ? response.errors : { oldPassword: response?.message || 'Failed to change password' }
  } finally {
    passwordSaving.value = false
  }
}

const passwordFormErrors = computed(() => passwordError.value || {})
const passwordLoading = computed(() => passwordSaving.value)
function hasPasswordError(field: keyof ChangePasswordFormErrors): boolean {
  return !!(passwordError.value && passwordError.value[field])
}
</script>

<template>
  <div class="account-tab" data-testid="account-tab">
    <section class="studio-card" aria-labelledby="profile-title">
      <h2 id="profile-title" class="card-title">Profile</h2>
      <Message v-if="accountError" severity="error" :closable="false">{{ accountError }}</Message>
      <dl class="facts">
        <dt>Email</dt><dd data-testid="profile-email">{{ email }}</dd>
        <dt>Display name</dt><dd data-testid="profile-name">{{ displayName }}</dd>
      </dl>
      <div class="field">
        <label for="publicUsername">Public username</label>
        <div class="row">
          <InputText
            id="publicUsername"
            v-model="publicUsername"
            placeholder="Your public profile name"
            :disabled="accountSaving"
            @input="checkUsernameAvailability"
            @blur="checkUsernameAvailability"
          />
          <Button v-if="!usernameChanged && publicUsername" label="View profile" icon="pi pi-external-link" outlined @click="goToPublicProfile" />
        </div>
        <small class="studio-muted" data-testid="username-hint">{{ usernameHint || 'Your public profile name in the app store.' }}</small>
      </div>
      <div class="actions">
        <Button
          label="Save"
          :loading="accountSaving"
          :disabled="!usernameChanged || accountSaving || !usernameAvailable"
          @click="saveAccountSettings"
        />
      </div>
    </section>

    <section class="studio-card" aria-labelledby="password-title">
      <h2 id="password-title" class="card-title">Password</h2>
      <p class="studio-muted lede" data-testid="password-rule">{{ passwordRule }}</p>
      <div class="actions left"><Button label="Change password" outlined @click="openPasswordDialog" /></div>
    </section>

    <section class="studio-card" aria-labelledby="appearance-title">
      <h2 id="appearance-title" class="card-title">Appearance</h2>
      <div class="field">
        <label>Theme</label>
        <SelectButton
          :model-value="themePreference"
          :options="themeOptions"
          option-label="label"
          option-value="value"
          :allow-empty="false"
          :disabled="settingsSaving"
          @update:model-value="handleThemeChange"
        >
          <template #option="{ option }"><i :class="option.icon" class="mr-2"></i><span>{{ option.label }}</span></template>
        </SelectButton>
        <small class="studio-muted">Saves when you pick one. System follows your device.</small>
      </div>
      <div class="field">
        <label for="editorFontSize">Editor font size</label>
        <InputNumber
          id="editorFontSize"
          :model-value="editorFontSize"
          :min="10"
          :max="24"
          show-buttons
          button-layout="horizontal"
          suffix=" px"
          :step="1"
          class="font-size"
          :disabled="settingsSaving || settingsLoading"
          @update:model-value="handleFontSize"
        >
          <template #incrementbuttonicon><i class="pi pi-plus" /></template>
          <template #decrementbuttonicon><i class="pi pi-minus" /></template>
        </InputNumber>
        <small class="studio-muted">10 to 24 px, used in code editors. Saves when you change it.</small>
      </div>
    </section>

    <Dialog
      v-model:visible="showPasswordDialog"
      header="Change password"
      :modal="true"
      :closable="!passwordLoading"
      :style="{ width: '400px', maxWidth: '95vw' }"
    >
      <Message v-if="passwordSuccess" severity="success" :closable="false">Password changed successfully!</Message>
      <form v-else class="flex flex-column gap-4" @submit.prevent="handlePasswordSubmit">
        <p class="studio-muted m-0">{{ passwordRule }}</p>
        <div class="flex flex-column gap-2">
          <label for="oldPassword" class="font-medium">Current password</label>
          <Password id="oldPassword" v-model="oldPassword" :feedback="false" toggle-mask fluid :invalid="hasPasswordError('oldPassword')" :disabled="passwordLoading" @input="passwordError = null" />
          <small v-if="hasPasswordError('oldPassword')" class="error">{{ passwordFormErrors.oldPassword }}</small>
        </div>
        <div class="flex flex-column gap-2">
          <label for="newPassword" class="font-medium">New password</label>
          <Password id="newPassword" v-model="newPassword" :feedback="false" toggle-mask fluid :invalid="hasPasswordError('newPassword')" :disabled="passwordLoading" @input="passwordError = null" />
          <small v-if="hasPasswordError('newPassword')" class="error">{{ passwordFormErrors.newPassword }}</small>
        </div>
        <div class="flex flex-column gap-2">
          <label for="confirmNewPassword" class="font-medium">Confirm new password</label>
          <Password id="confirmNewPassword" v-model="confirmPassword" :feedback="false" toggle-mask fluid :invalid="hasPasswordError('confirmPassword')" :disabled="passwordLoading" @input="passwordError = null" />
          <small v-if="hasPasswordError('confirmPassword')" class="error">{{ passwordFormErrors.confirmPassword }}</small>
        </div>
      </form>
      <template #footer>
        <template v-if="!passwordSuccess">
          <Button label="Cancel" text :disabled="passwordLoading" @click="showPasswordDialog = false" />
          <Button label="Change password" :loading="passwordLoading" :disabled="passwordLoading" @click="handlePasswordSubmit" />
        </template>
      </template>
    </Dialog>
  </div>
</template>

<style scoped>
.account-tab { display: flex; flex-direction: column; }
.card-title { margin: 0; font-family: var(--display); font-size: 16px; font-weight: 600; color: var(--ink); }
.lede { margin: 0; }
.facts { display: grid; grid-template-columns: max-content 1fr; gap: 6px 24px; margin: 0; }
.facts dt { color: var(--ink-soft); }
.facts dd { margin: 0; color: var(--ink); overflow-wrap: anywhere; }
.field { display: flex; flex-direction: column; gap: 6px; }
.field label { font-weight: 500; }
.row { display: flex; gap: 8px; flex-wrap: wrap; }
.row :deep(.p-inputtext) { flex: 1 1 240px; max-width: 360px; }
.actions { display: flex; justify-content: flex-end; }
.actions.left { justify-content: flex-start; }
.font-size { width: 10rem; }
.error { color: var(--danger, #b42318); }
</style>
