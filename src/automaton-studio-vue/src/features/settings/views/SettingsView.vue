<script setup lang="ts">
import { ref, onMounted, watch } from 'vue'
import Card from 'primevue/card'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import InputNumber from 'primevue/inputnumber'
import Password from 'primevue/password'
import SelectButton from 'primevue/selectbutton'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Tag from 'primevue/tag'
import Dialog from 'primevue/dialog'
import Message from 'primevue/message'
import { useToast } from 'primevue/usetoast'
import { useTheme } from '@/shared/composables/useTheme'
import { useEditorSettings } from '@/shared/composables/useEditorSettings'
import { useAuthStore } from '@/features/auth/stores/auth.store'
import { authService } from '@/features/auth/services/auth.service'
import type { ChangePasswordFormErrors } from '@/features/auth/types/auth.types'

const { themePreference, setTheme, isSaving: themeSaving } = useTheme()
const {
  settings: editorSettings,
  isLoading: editorSettingsLoading,
  isSaving: editorSettingsSaving,
  setFontSize,
} = useEditorSettings()
const authStore = useAuthStore()
const toast = useToast()

const activeSection = ref('account')

// Account settings - use actual user data
const displayName = ref('')
const email = ref('')

// Local editor font size for input binding (synced from cached settings)
const editorFontSize = ref(14)

onMounted(() => {
  // Load user data from store
  if (authStore.user) {
    displayName.value = authStore.user.name || ''
    email.value = authStore.user.email || ''
  }
  // Sync local value from cached settings
  editorFontSize.value = editorSettings.value.fontSize
})

// Keep local value in sync with cached settings
watch(
  () => editorSettings.value.fontSize,
  (newSize) => {
    editorFontSize.value = newSize
  }
)

// Theme settings
const themeOptions = ref([
  { label: 'Light', value: 'light', icon: 'pi pi-sun' },
  { label: 'Dark', value: 'dark', icon: 'pi pi-moon' },
  { label: 'System', value: 'system', icon: 'pi pi-desktop' },
])

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

// API Keys mock data
const apiKeys = ref([
  { id: 1, name: 'Production API Key', key: 'ak_prod_****', scope: 'Execute', created: '2024-01-15', lastUsed: '5 min ago' },
  { id: 2, name: 'Runner Key', key: 'ak_runner_****', scope: 'Runner', created: '2024-01-10', lastUsed: '2 hours ago' },
])

// AI Keys mock data
const aiKeys = ref([
  { id: 1, provider: 'OpenAI', model: 'gpt-4', configured: true },
  { id: 2, provider: 'Anthropic', model: 'claude-3', configured: false },
  { id: 3, provider: 'Google', model: 'gemini-pro', configured: false },
])

// Change password dialog state
const showPasswordDialog = ref(false)
const oldPassword = ref('')
const newPassword = ref('')
const confirmPassword = ref('')
const passwordFormErrors = ref<ChangePasswordFormErrors>({})
const passwordSubmitted = ref(false)
const passwordLoading = ref(false)
const passwordError = ref<string | null>(null)
const passwordSuccess = ref(false)

const handleThemeChange = (value: string) => {
  if (value === 'light' || value === 'dark' || value === 'system') {
    setTheme(value)
  }
}

// Open password change dialog
function openPasswordDialog() {
  // Reset form state
  oldPassword.value = ''
  newPassword.value = ''
  confirmPassword.value = ''
  passwordFormErrors.value = {}
  passwordSubmitted.value = false
  passwordError.value = null
  passwordSuccess.value = false
  showPasswordDialog.value = true
}

// Validate password form
function validatePasswordForm(): boolean {
  passwordFormErrors.value = {}
  let isValid = true

  if (!oldPassword.value) {
    passwordFormErrors.value.oldPassword = 'Current password is required'
    isValid = false
  }

  if (!newPassword.value) {
    passwordFormErrors.value.newPassword = 'New password is required'
    isValid = false
  } else if (newPassword.value.length < 6) {
    passwordFormErrors.value.newPassword = 'Password must be at least 6 characters'
    isValid = false
  } else if (!/(?=.*[a-z])(?=.*[A-Z])(?=.*\d)/.test(newPassword.value)) {
    passwordFormErrors.value.newPassword = 'Password must contain uppercase, lowercase, and number'
    isValid = false
  }

  if (!confirmPassword.value) {
    passwordFormErrors.value.confirmPassword = 'Please confirm your password'
    isValid = false
  } else if (newPassword.value !== confirmPassword.value) {
    passwordFormErrors.value.confirmPassword = 'Passwords do not match'
    isValid = false
  }

  return isValid
}

// Submit password change
async function handlePasswordSubmit() {
  passwordSubmitted.value = true
  passwordError.value = null

  if (!validatePasswordForm()) {
    return
  }

  passwordLoading.value = true

  try {
    await authService.changePassword({
      oldPassword: oldPassword.value,
      newPassword: newPassword.value,
    })
    passwordSuccess.value = true
    // Close dialog after 2 seconds
    setTimeout(() => {
      showPasswordDialog.value = false
    }, 2000)
  } catch (err: unknown) {
    const axiosError = err as { response?: { data?: { message?: string }; status?: number } }
    if (axiosError.response?.status === 400 || axiosError.response?.status === 401) {
      passwordError.value = 'Current password is incorrect'
    } else if (axiosError.response?.data?.message) {
      passwordError.value = axiosError.response.data.message
    } else {
      passwordError.value = 'Failed to change password. Please try again.'
    }
  } finally {
    passwordLoading.value = false
  }
}

// Check if field has error
function hasPasswordError(field: keyof ChangePasswordFormErrors): boolean {
  return passwordSubmitted.value && !!passwordFormErrors.value[field]
}

// Clear password error
function clearPasswordError() {
  passwordError.value = null
}
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
              <Button
                label="API Keys"
                icon="pi pi-key"
                text
                :class="{ 'surface-hover': activeSection === 'apikeys' }"
                class="w-full justify-content-start"
                @click="activeSection = 'apikeys'"
              />
              <Button
                label="AI Keys"
                icon="pi pi-sparkles"
                text
                :class="{ 'surface-hover': activeSection === 'aikeys' }"
                class="w-full justify-content-start"
                @click="activeSection = 'aikeys'"
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
              <div class="flex flex-column gap-2">
                <label for="email" class="font-medium">Email</label>
                <InputText id="email" v-model="email" class="w-full" disabled />
              </div>

              <div class="flex flex-column gap-2">
                <label for="displayName" class="font-medium">Display Name</label>
                <InputText id="displayName" v-model="displayName" class="w-full" />
              </div>

              <div class="flex flex-column gap-2">
                <label class="font-medium">Password</label>
                <Button
                  label="Change Password"
                  outlined
                  class="w-auto align-self-start"
                  @click="openPasswordDialog"
                />
              </div>

              <div class="flex justify-content-end">
                <Button label="Save Changes" />
              </div>
            </div>
          </template>
        </Card>

        <!-- API Keys Section -->
        <Card v-if="activeSection === 'apikeys'">
          <template #title>
            <div class="flex align-items-center justify-content-between">
              <span>API Keys</span>
              <Button label="Generate New Key" icon="pi pi-plus" size="small" />
            </div>
          </template>
          <template #subtitle>
            Manage API keys for runners, webhooks, and integrations
          </template>
          <template #content>
            <DataTable :value="apiKeys" class="p-datatable-sm">
              <Column field="name" header="Name"></Column>
              <Column field="key" header="Key">
                <template #body="{ data }">
                  <code class="surface-ground p-1 border-round">{{ data.key }}</code>
                </template>
              </Column>
              <Column field="scope" header="Scope">
                <template #body="{ data }">
                  <Tag :value="data.scope" severity="secondary" />
                </template>
              </Column>
              <Column field="lastUsed" header="Last Used"></Column>
              <Column header="Actions">
                <template #body>
                  <div class="flex gap-1">
                    <Button icon="pi pi-copy" text rounded size="small" severity="secondary" />
                    <Button icon="pi pi-trash" text rounded size="small" severity="danger" />
                  </div>
                </template>
              </Column>
            </DataTable>
          </template>
        </Card>

        <!-- AI Keys Section -->
        <Card v-if="activeSection === 'aikeys'">
          <template #title>AI Provider Keys</template>
          <template #subtitle>
            Configure API keys for AI-powered features
          </template>
          <template #content>
            <div class="flex flex-column gap-4">
              <div
                v-for="provider in aiKeys"
                :key="provider.id"
                class="flex align-items-center justify-content-between p-3 surface-ground border-round"
              >
                <div class="flex align-items-center gap-3">
                  <div
                    class="flex align-items-center justify-content-center border-round-lg"
                    style="width: 40px; height: 40px; background-color: var(--p-primary-100)"
                  >
                    <i class="pi pi-sparkles" style="color: var(--p-primary-600)"></i>
                  </div>
                  <div>
                    <div class="font-semibold">{{ provider.provider }}</div>
                    <div class="text-color-secondary text-sm">{{ provider.model }}</div>
                  </div>
                </div>
                <div class="flex align-items-center gap-3">
                  <Tag
                    :value="provider.configured ? 'Configured' : 'Not Configured'"
                    :severity="provider.configured ? 'success' : 'secondary'"
                  />
                  <Button
                    :label="provider.configured ? 'Update' : 'Configure'"
                    size="small"
                    :outlined="provider.configured"
                  />
                </div>
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
                  :disabled="themeSaving"
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
                    :disabled="editorSettingsSaving || editorSettingsLoading"
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
                  :loading="editorSettingsSaving"
                  :disabled="editorSettingsSaving || editorSettingsLoading"
                  @click="saveEditorSettings"
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
  max-width: 1200px;
  margin: 0 auto;
}
</style>
