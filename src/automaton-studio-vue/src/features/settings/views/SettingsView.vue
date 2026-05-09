<script setup lang="ts">
import { ref, onMounted, watch, computed } from 'vue'
import { useRouter } from 'vue-router'
import Card from 'primevue/card'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import InputNumber from 'primevue/inputnumber'
import Textarea from 'primevue/textarea'
import Password from 'primevue/password'
import SelectButton from 'primevue/selectbutton'
import Select from 'primevue/select'
import Dialog from 'primevue/dialog'
import Message from 'primevue/message'
import Tag from 'primevue/tag'
import { useToast } from 'primevue/usetoast'
import { useUserSettings } from '@/shared/composables/useUserSettings'
import { useAuthStore } from '@/features/auth/stores/auth.store'
import { authService } from '@/features/auth/services/auth.service'
import { getAvailableProviders } from '@/shared/services/ai/ai-providers-service'
import type { AiProvider } from '@/shared/types/ai.types'
import type { ChangePasswordFormErrors } from '@/features/auth/types/auth.types'
import { AppExecutionPolicy, AppVerificationStrategy } from '../types/settings.types'
const {
  theme: themePreference,
  fontSize,
  defaultAiModel,
  defaultAiProvider,
  defaultPlanningModel,
  defaultPlanningProvider,
  showAiDebugDetails,
  appVerificationStrategy,
  appExecutionPolicy,
  isLoading: settingsLoading,
  isSaving: settingsSaving,
  setTheme,
  setFontSize,
  setDefaultAiModel,
  setDefaultPlanningModel,
  setShowAiDebugDetails,
  setAppVerificationStrategy,
  setAppExecutionPolicy,
} = useUserSettings()
const authStore = useAuthStore()
const router = useRouter()
const toast = useToast()

const activeSection = ref('account')

// Account settings - use actual user data
const displayName = ref('')
const email = ref('')
const publicUsername = ref('')
const originalPublicUsername = ref('')

// Public username management
const checkingUsername = ref(false)
const usernameAvailable = ref<boolean | null>(null)
const accountSaving = ref(false)
const accountError = ref<string | null>(null)
const accountSuccess = ref(false)

// Local editor font size for input binding (synced from cached settings)
const editorFontSize = ref(14)

// AI preferences
const aiProviders = ref<AiProvider[]>([])
const aiModelsLoading = ref(false)

const aiModelOptions = computed(() =>
  aiProviders.value.map((provider) => ({
    label: provider.displayName || provider.providerName,
    providerName: provider.providerName,
    items: provider.supportedModels.map((model) => ({ label: model, value: model })),
  })),
)

const aiModelFlatOptions = computed(() =>
  aiProviders.value.flatMap((provider) =>
    provider.supportedModels.map((model) => ({
      label: model,
      value: model,
      provider: provider.providerName,
    })),
  ),
)

const selectedDefaultAiModel = ref<string | undefined>(undefined)
const selectedDefaultPlanningModel = ref<string | undefined>(undefined)
const aiDebugDetailsEnabled = ref(false)
const selectedAppVerificationStrategy = ref<string | undefined>(undefined)
const selectedAppExecutionPolicy = ref<string | undefined>(undefined)
const appVerificationStrategyOptions = [
  { label: 'Balanced', value: AppVerificationStrategy.Balanced },
  { label: 'Continuous', value: AppVerificationStrategy.Continuous },
  { label: 'Final Gate', value: AppVerificationStrategy.FinalGate },
]
const appExecutionPolicyOptions = [
  { label: 'Default', value: AppExecutionPolicy.Default },
  { label: 'Lean Tools', value: AppExecutionPolicy.LeanTools },
]

onMounted(async () => {
  // Load user data from store
  if (authStore.user) {
    displayName.value = authStore.user.name || ''
    email.value = authStore.user.email || ''
    if (authStore.user.publicUsername) {
      publicUsername.value = authStore.user.publicUsername
      originalPublicUsername.value = authStore.user.publicUsername
    }
  }
  // Sync local value from cached settings
  editorFontSize.value = fontSize.value
  selectedDefaultAiModel.value = defaultAiModel.value
  selectedDefaultPlanningModel.value = defaultPlanningModel.value
  aiDebugDetailsEnabled.value = showAiDebugDetails.value
  selectedAppVerificationStrategy.value = appVerificationStrategy.value
  selectedAppExecutionPolicy.value = appExecutionPolicy.value

  // Load AI providers for model dropdowns
  aiModelsLoading.value = true
  try {
    const providers = await getAvailableProviders(true)
    aiProviders.value = providers.filter(
      (p) => (p.hasStoredKeys || p.hasEnvironmentKey) && p.supportedModels.length > 0,
    )
  } catch {
    // Non-critical — dropdowns will be empty
  } finally {
    aiModelsLoading.value = false
  }
})

// Keep local value in sync with cached settings
watch(fontSize, (newSize) => {
  editorFontSize.value = newSize
})

watch(defaultAiModel, (v) => { selectedDefaultAiModel.value = v })
watch(defaultPlanningModel, (v) => { selectedDefaultPlanningModel.value = v })
watch(showAiDebugDetails, (v) => { aiDebugDetailsEnabled.value = v })
watch(appVerificationStrategy, (v) => { selectedAppVerificationStrategy.value = v })
watch(appExecutionPolicy, (v) => { selectedAppExecutionPolicy.value = v })

// Check username availability
async function checkUsernameAvailability() {
  if (publicUsername.value === originalPublicUsername.value || !publicUsername.value) {
    usernameAvailable.value = null
    return
  }

  if (publicUsername.value.length < 3) {
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

// Save account settings
async function saveAccountSettings() {
  accountError.value = null
  accountSuccess.value = false

  try {
    // Validate public username if changed
    if (publicUsername.value !== originalPublicUsername.value) {
      if (!publicUsername.value || publicUsername.value.length < 3 || publicUsername.value.length > 32) {
        accountError.value = 'Public username must be 3-32 characters'
        return
      }

      if (!/^[a-zA-Z0-9_\-]+$/.test(publicUsername.value)) {
        accountError.value = 'Public username can only contain letters, numbers, hyphens, and underscores'
        return
      }

      if (!usernameAvailable.value) {
        accountError.value = 'This public username is already taken'
        return
      }

      // Update public username
      accountSaving.value = true
      await authService.updatePublicUsername(publicUsername.value)
      authStore.setPublicUsername(publicUsername.value)
      await authStore.fetchUserProfile()
      originalPublicUsername.value = publicUsername.value
    }

    accountSuccess.value = true
    toast.add({
      severity: 'success',
      summary: 'Settings Saved',
      detail: 'Your account settings have been updated.',
      life: 3000,
    })
  } catch (err: any) {
    console.error('Failed to save account settings:', err)
    // Try to extract ModelState errors from backend
    let errorMsg = 'Failed to save account settings. Please try again.'
    const response = err?.response?.data
    if (response) {
      // ASP.NET ModelState returns errors as { errors: { Field: [msg] } }
      if (response.errors) {
        // Prefer NewPublicUsername errors if present
        if (response.errors.NewPublicUsername && response.errors.NewPublicUsername.length > 0) {
          errorMsg = response.errors.NewPublicUsername[0]
        } else {
          // Fallback: first error message
          const firstKey = Object.keys(response.errors)[0]
          if (firstKey && response.errors[firstKey].length > 0) {
            errorMsg = response.errors[firstKey][0]
          }
        }
      } else if (response.message) {
        errorMsg = response.message
      }
    }
    accountError.value = errorMsg
    toast.add({
      severity: 'error',
      summary: 'Save Failed',
      detail: errorMsg,
      life: 5000,
    })
  } finally {
    accountSaving.value = false
  }
}

// Navigate to public profile
function goToPublicProfile() {
  if (publicUsername.value) {
    router.push(`/${publicUsername.value}`)
  }
}

// Theme settings
const themeOptions = ref([
  { label: 'Light', value: 'light', icon: 'pi pi-sun' },
  { label: 'Dark', value: 'dark', icon: 'pi pi-moon' },
  { label: 'System', value: 'system', icon: 'pi pi-desktop' },
])

async function saveAiPreferences() {
  try {
    const aiModel = selectedDefaultAiModel.value
    const aiProv = aiModel
      ? (aiModelFlatOptions.value.find((o) => o.value === aiModel)?.provider ?? '')
      : undefined

    const planModel = selectedDefaultPlanningModel.value
    const planProv = planModel
      ? (aiModelFlatOptions.value.find((o) => o.value === planModel)?.provider ?? '')
      : undefined

    // Save sequentially: settings composable uses a shared isSaving gate.
    // Parallel calls can skip updates and leave some values unsaved.
    await setDefaultAiModel(aiModel, aiProv)
    await setDefaultPlanningModel(planModel, planProv)
    await setShowAiDebugDetails(aiDebugDetailsEnabled.value)
    await setAppVerificationStrategy(selectedAppVerificationStrategy.value)
    await setAppExecutionPolicy(selectedAppExecutionPolicy.value)

    toast.add({
      severity: 'success',
      summary: 'Settings Saved',
      detail: 'AI preferences have been updated.',
      life: 3000,
    })
  } catch {
    toast.add({
      severity: 'error',
      summary: 'Save Failed',
      detail: 'Could not save AI preferences.',
      life: 5000,
    })
  }
}

async function saveEditorSettings() {
  try {
    await setFontSize(editorFontSize.value)
    toast.add({
      severity: 'success',
      summary: 'Settings Saved',
      detail: 'Editor settings have been updated.',
      life: 3000,
    })
  } catch (err) {
    console.error('Failed to save editor settings:', err)
    toast.add({
      severity: 'error',
      summary: 'Save Failed',
      detail: 'Could not save editor settings.',
      life: 5000,
    })
  }
}

// Change password dialog state
const showPasswordDialog = ref(false)
const oldPassword = ref('')
const newPassword = ref('')
const confirmPassword = ref('')
const passwordSaving = ref(false)
const passwordError = ref<ChangePasswordFormErrors | null>(null)
const passwordSuccess = ref(false)

// Clear password error
function clearPasswordError() {
  passwordError.value = null
}

// Open password change dialog
function openPasswordDialog() {
  showPasswordDialog.value = true
  oldPassword.value = ''
  newPassword.value = ''
  confirmPassword.value = ''
  passwordError.value = null
  passwordSuccess.value = false
}

// Handle theme change
async function handleThemeChange(newTheme: string) {
  await setTheme(newTheme as 'light' | 'dark' | 'system')
}

// Handle password submission
async function handlePasswordSubmit() {
  passwordError.value = null
  passwordSuccess.value = false

  // Validate fields
  const errors: ChangePasswordFormErrors = {}
  if (!oldPassword.value) {
    errors.oldPassword = 'Current password is required'
  }
  if (!newPassword.value || newPassword.value.length < 8) {
    errors.newPassword = 'New password must be at least 8 characters'
  }
  if (newPassword.value !== confirmPassword.value) {
    errors.confirmPassword = 'Passwords do not match'
  }

  if (Object.keys(errors).length > 0) {
    passwordError.value = errors
    return
  }

  passwordSaving.value = true
  try {
    await authService.changePassword({
      oldPassword: oldPassword.value,
      newPassword: newPassword.value,
    })

    passwordSuccess.value = true
    toast.add({
      severity: 'success',
      summary: 'Password Changed',
      detail: 'Your password has been updated successfully.',
      life: 3000,
    })

    setTimeout(() => {
      showPasswordDialog.value = false
    }, 1500)
  } catch (err: any) {
    const response = err?.response?.data
    if (response?.errors) {
      passwordError.value = response.errors
    } else {
      passwordError.value = { oldPassword: response?.message || 'Failed to change password' }
    }
  } finally {
    passwordSaving.value = false
  }
}

// Check if password field has error
function hasPasswordError(field: keyof ChangePasswordFormErrors): boolean {
  return !!(passwordError.value && passwordError.value[field])
}

// Computed for password form errors (for template access)
const passwordFormErrors = computed(() => passwordError.value || {})
const passwordLoading = computed(() => passwordSaving.value)

</script>

<template>
  <div class="settings-view">
    <!-- Page Header -->
    <div class="mb-5">
      <h1 class="text-3xl font-bold m-0">Settings</h1>
      <p class="text-color-secondary mt-1 mb-0">Manage your account and preferences</p>
    </div>

    <div class="grid">
      <!-- Sidebar -->
      <div class="col-12 lg:col-3">
        <Card>
          <template #content>
            <div class="flex flex-column gap-1">
              <div class="text-color-secondary text-sm font-semibold mb-2 px-3">Profile</div>
              <Button
                label="Account"
                icon="pi pi-user"
                text
                :class="{ 'surface-hover': activeSection === 'account' }"
                class="w-full justify-content-start"
                @click="activeSection = 'account'"
              />
              <div class="text-color-secondary text-sm font-semibold mb-2 mt-4 px-3">Preferences</div>
              <Button
                label="Appearance"
                icon="pi pi-palette"
                text
                :class="{ 'surface-hover': activeSection === 'appearance' }"
                class="w-full justify-content-start"
                @click="activeSection = 'appearance'"
              />
              <Button
                label="AI"
                icon="pi pi-sparkles"
                text
                :class="{ 'surface-hover': activeSection === 'ai' }"
                class="w-full justify-content-start"
                @click="activeSection = 'ai'"
              />
              <Button
                label="Notifications"
                icon="pi pi-bell"
                text
                :class="{ 'surface-hover': activeSection === 'notifications' }"
                class="w-full justify-content-start"
                @click="activeSection = 'notifications'"
              />
            </div>
          </template>
        </Card>
      </div>

      <!-- Content -->
      <div class="col-12 lg:col-9">
        <!-- Account Section -->
        <Card v-if="activeSection === 'account'">
          <template #title>Account Settings</template>
          <template #content>
            <div class="flex flex-column gap-4">
              <Message
                v-if="accountError"
                severity="error"
                :closable="false"
                class="mb-2"
              >
                {{ accountError }}
              </Message>

              <Message v-if="accountSuccess" severity="success" :closable="true" class="mb-2">
                Settings saved successfully!
              </Message>

              <div class="flex flex-column gap-2">
                <label for="email" class="font-medium">Email</label>
                <InputText id="email" v-model="email" class="w-full" disabled />
              </div>

              <div class="flex flex-column gap-2">
                <label for="displayName" class="font-medium">Display Name</label>
                <InputText id="displayName" v-model="displayName" class="w-full" disabled />
                <small class="text-color-secondary">Contact support to change your display name</small>
              </div>

              <div class="flex flex-column gap-2">
                <div class="flex align-items-center justify-content-between">
                  <label for="publicUsername" class="font-medium">Public Username</label>
                  <Tag
                    v-if="publicUsername !== originalPublicUsername && publicUsername.length >= 3 && usernameAvailable"
                    severity="success"
                    value="Available"
                    class="text-xs"
                  />
                  <Tag
                    v-else-if="publicUsername !== originalPublicUsername && publicUsername.length >= 3 && usernameAvailable === false"
                    severity="danger"
                    value="Taken"
                    class="text-xs"
                  />
                  <Tag
                    v-else-if="publicUsername !== originalPublicUsername && publicUsername.length >= 3 && checkingUsername"
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
                  :disabled="accountSaving"
                  @input="checkUsernameAvailability"
                  @blur="checkUsernameAvailability"
                />
                <small class="text-color-secondary text-xs">
                  Your public profile: https://automaton.monster/{{ publicUsername }}
                </small>
                <small v-if="publicUsername.length < 3 && publicUsername" class="text-orange-500 text-xs">
                  At least 3 characters required
                </small>
                <Button
                  v-if="publicUsername === originalPublicUsername"
                  label="View Profile"
                  text
                  icon="pi pi-external-link"
                  class="w-auto align-self-start p-0"
                  @click="goToPublicProfile"
                />
              </div>

              <div class="flex flex-column gap-2">
                <label class="font-medium">Password</label>
                <Button
                  label="Change Password"
                  outlined
                  class="w-auto align-self-start"
                  :disabled="accountSaving"
                  @click="openPasswordDialog"
                />
              </div>

              <div class="flex justify-content-end gap-2">
                <Button
                  label="Save Changes"
                  :loading="accountSaving"
                  :disabled="publicUsername === originalPublicUsername || accountSaving || (publicUsername !== originalPublicUsername && !usernameAvailable)"
                  @click="saveAccountSettings"
                />
              </div>

            </div>
          </template>
        </Card>
        <!-- Appearance Section -->
        <Card v-if="activeSection === 'appearance'">
          <template #title>Appearance</template>
          <template #content>
            <div class="flex flex-column gap-5">
              <!-- Theme -->
              <div>
                <label class="font-medium block mb-3">Theme</label>
                <SelectButton
                  :model-value="themePreference"
                  :options="themeOptions"
                  option-label="label"
                  option-value="value"
                  :disabled="settingsSaving"
                  @update:model-value="handleThemeChange"
                >
                  <template #option="{ option }">
                    <i :class="option.icon" class="mr-2"></i>
                    <span>{{ option.label }}</span>
                  </template>
                </SelectButton>
                <small class="text-color-secondary block mt-2">
                  Choose your preferred color scheme. "System" follows your device settings.
                </small>
              </div>

              <!-- Editor Font Size -->
              <div class="flex flex-column gap-2">
                <label for="editorFontSize" class="font-medium">Editor Font Size</label>
                <div class="flex align-items-center gap-3">
                  <InputNumber
                    id="editorFontSize"
                    v-model="editorFontSize"
                    :min="10"
                    :max="24"
                    show-buttons
                    button-layout="horizontal"
                    suffix=" px"
                    :step="1"
                    class="w-10rem"
                    :disabled="settingsSaving || settingsLoading"
                  >
                    <template #incrementbuttonicon>
                      <i class="pi pi-plus" />
                    </template>
                    <template #decrementbuttonicon>
                      <i class="pi pi-minus" />
                    </template>
                  </InputNumber>
                  <span class="text-color-secondary text-sm">(10-24 px)</span>
                </div>
                <small class="text-color-secondary">
                  Font size used in code editors for flows, steps, and apps.
                </small>
              </div>

              <!-- Save Button -->
              <div class="flex justify-content-end pt-3 border-top-1 surface-border">
                <Button
                  label="Save Changes"
                  icon="pi pi-check"
                  :loading="settingsSaving"
                  :disabled="settingsSaving || settingsLoading"
                  @click="saveEditorSettings"
                />
              </div>
            </div>
          </template>
        </Card>

        <!-- AI Preferences Section -->
        <Card v-if="activeSection === 'ai'">
          <template #title>AI Preferences</template>
          <template #content>
            <div class="flex flex-column gap-5">
              <p class="text-color-secondary m-0">
                Set your preferred models. These override the admin defaults and apply across all AI assistants.
              </p>

              <!-- Default AI Model -->
              <div class="flex flex-column gap-2">
                <label for="defaultAiModel" class="font-medium">Default AI Model</label>
                <Select
                  id="defaultAiModel"
                  v-model="selectedDefaultAiModel"
                  :options="aiModelOptions"
                  option-label="label"
                  option-value="value"
                  option-group-label="label"
                  option-group-children="items"
                  placeholder="Use admin default"
                  show-clear
                  :loading="aiModelsLoading"
                  :disabled="settingsSaving || aiModelsLoading"
                  class="w-full"
                />
                <small class="text-color-secondary">
                  Used for chat, code generation, and general AI tasks.
                </small>
              </div>

              <!-- Default Planning Model -->
              <div class="flex flex-column gap-2">
                <label for="defaultPlanningModel" class="font-medium">Default Planning Model</label>
                <Select
                  id="defaultPlanningModel"
                  v-model="selectedDefaultPlanningModel"
                  :options="aiModelOptions"
                  option-label="label"
                  option-value="value"
                  option-group-label="label"
                  option-group-children="items"
                  placeholder="Use admin default"
                  show-clear
                  :loading="aiModelsLoading"
                  :disabled="settingsSaving || aiModelsLoading"
                  class="w-full"
                />
                <small class="text-color-secondary">
                  Used for multi-step planning and flow generation tasks.
                </small>
              </div>

              <!-- AI Chat Debug Rendering -->
              <div class="flex align-items-center justify-content-between p-3 surface-ground border-round">
                <div class="pr-3">
                  <div class="font-medium">Show AI Debug Details</div>
                  <div class="text-color-secondary text-sm">
                    Display verbose tool instructions/results and UI check messages in AI chat. This only changes what is shown, not what is sent to AI.
                  </div>
                </div>
                <SelectButton
                  :model-value="aiDebugDetailsEnabled"
                  :options="[
                    { label: 'Off', value: false },
                    { label: 'On', value: true },
                  ]"
                  option-label="label"
                  option-value="value"
                  :disabled="settingsSaving || aiModelsLoading"
                  @update:model-value="(v) => aiDebugDetailsEnabled = Boolean(v)"
                />
              </div>

              <div class="flex flex-column gap-2">
                <label for="appVerificationStrategy" class="font-medium">App Verification Strategy</label>
                <Select
                  id="appVerificationStrategy"
                  v-model="selectedAppVerificationStrategy"
                  :options="appVerificationStrategyOptions"
                  option-label="label"
                  option-value="value"
                  placeholder="Use organization/admin default"
                  show-clear
                  :disabled="settingsSaving"
                  class="w-full"
                />
                <small class="text-color-secondary">
                  Personal override for how aggressively the app builder verifies each step.
                </small>
              </div>

              <div class="flex flex-column gap-2">
                <label for="appExecutionPolicy" class="font-medium">App Execution Policy</label>
                <Select
                  id="appExecutionPolicy"
                  v-model="selectedAppExecutionPolicy"
                  :options="appExecutionPolicyOptions"
                  option-label="label"
                  option-value="value"
                  placeholder="Use organization/admin default"
                  show-clear
                  :disabled="settingsSaving"
                  class="w-full"
                />
                <small class="text-color-secondary">
                  Personal override for tool-call efficiency behavior during app generation.
                </small>
              </div>

              <div class="flex justify-content-end pt-3 border-top-1 surface-border">
                <Button
                  label="Save Changes"
                  icon="pi pi-check"
                  :loading="settingsSaving"
                  :disabled="settingsSaving || aiModelsLoading"
                  @click="saveAiPreferences"
                />
              </div>
            </div>
          </template>
        </Card>

        <!-- Notifications Section -->
        <Card v-if="activeSection === 'notifications'">
          <template #title>Notification Preferences</template>
          <template #content>
            <div class="flex flex-column gap-4">
              <div class="flex align-items-center justify-content-between p-3 surface-ground border-round">
                <div>
                  <div class="font-medium">Email Notifications</div>
                  <div class="text-color-secondary text-sm">Receive emails for flow failures</div>
                </div>
                <Button label="Configure" outlined size="small" />
              </div>

              <div class="flex align-items-center justify-content-between p-3 surface-ground border-round">
                <div>
                  <div class="font-medium">Slack Notifications</div>
                  <div class="text-color-secondary text-sm">Send notifications to Slack channel</div>
                </div>
                <Button label="Connect" outlined size="small" />
              </div>

              <div class="flex align-items-center justify-content-between p-3 surface-ground border-round">
                <div>
                  <div class="font-medium">Webhook Notifications</div>
                  <div class="text-color-secondary text-sm">POST to custom endpoint</div>
                </div>
                <Button label="Configure" outlined size="small" />
              </div>
            </div>
          </template>
        </Card>
      </div>
    </div>

    <!-- Change Password Dialog -->
    <Dialog
      v-model:visible="showPasswordDialog"
      header="Change Password"
      :modal="true"
      :closable="!passwordLoading"
      :style="{ width: '400px' }"
    >
      <template v-if="passwordSuccess">
        <Message severity="success" :closable="false">
          Password changed successfully!
        </Message>
      </template>

      <template v-else>
        <Message v-if="passwordError" severity="error" :closable="false" class="mb-4">
          {{ passwordError }}
        </Message>

        <form class="flex flex-column gap-4" @submit.prevent="handlePasswordSubmit">
          <div class="flex flex-column gap-2">
            <label for="oldPassword" class="font-medium">Current Password</label>
            <Password
              id="oldPassword"
              v-model="oldPassword"
              placeholder="Enter current password"
              :feedback="false"
              toggle-mask
              class="w-full"
              input-class="w-full"
              :invalid="hasPasswordError('oldPassword')"
              :disabled="passwordLoading"
              @input="clearPasswordError"
            />
            <small v-if="hasPasswordError('oldPassword')" class="text-red-500">
              {{ passwordFormErrors.oldPassword }}
            </small>
          </div>

          <div class="flex flex-column gap-2">
            <label for="newPassword" class="font-medium">New Password</label>
            <Password
              id="newPassword"
              v-model="newPassword"
              placeholder="Enter new password"
              toggle-mask
              class="w-full"
              input-class="w-full"
              :invalid="hasPasswordError('newPassword')"
              :disabled="passwordLoading"
              @input="clearPasswordError"
            />
            <small v-if="hasPasswordError('newPassword')" class="text-red-500">
              {{ passwordFormErrors.newPassword }}
            </small>
            <small v-else class="text-color-secondary">
              Must contain uppercase, lowercase, and number
            </small>
          </div>

          <div class="flex flex-column gap-2">
            <label for="confirmNewPassword" class="font-medium">Confirm New Password</label>
            <Password
              id="confirmNewPassword"
              v-model="confirmPassword"
              placeholder="Confirm new password"
              :feedback="false"
              toggle-mask
              class="w-full"
              input-class="w-full"
              :invalid="hasPasswordError('confirmPassword')"
              :disabled="passwordLoading"
              @input="clearPasswordError"
            />
            <small v-if="hasPasswordError('confirmPassword')" class="text-red-500">
              {{ passwordFormErrors.confirmPassword }}
            </small>
          </div>
        </form>
      </template>

      <template #footer>
        <template v-if="!passwordSuccess">
          <Button
            label="Cancel"
            text
            :disabled="passwordLoading"
            @click="showPasswordDialog = false"
          />
          <Button
            label="Change Password"
            :loading="passwordLoading"
            :disabled="passwordLoading"
            @click="handlePasswordSubmit"
          />
        </template>
      </template>
    </Dialog>

  </div>
</template>

<style scoped>
.settings-view {
  max-width: 1400px;
  margin: 0 auto;
}
</style>
