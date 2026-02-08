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
import Dialog from 'primevue/dialog'
import Message from 'primevue/message'
import Tag from 'primevue/tag'
import { useToast } from 'primevue/usetoast'
import { useUserSettings } from '@/shared/composables/useUserSettings'
import { useAuthStore } from '@/features/auth/stores/auth.store'
import { authService } from '@/features/auth/services/auth.service'
import type { ChangePasswordFormErrors } from '@/features/auth/types/auth.types'
import ApiKeysSection from '../components/ApiKeysSection.vue'
import AiKeysSection from '../components/AiKeysSection.vue'
// Organizations (CTA from Settings)
import { organizationsService } from '@/features/organizations/services/organizationsService'
import { useOrganizationRole } from '@/features/organizations/composables/useOrganizationRole'
import type { CreateOrganizationRequest } from '@/features/organizations/types'

const {
  theme: themePreference,
  fontSize,
  isLoading: settingsLoading,
  isSaving: settingsSaving,
  setTheme,
  setFontSize,
} = useUserSettings()
const authStore = useAuthStore()
const router = useRouter()
const toast = useToast()

// Organizations state (for progressive disclosure)
const { organizations, loadOrganizations } = useOrganizationRole()
const hasOrganizations = computed(() => organizations.value && organizations.value.length > 0)
const showCreateOrgDialog = ref(false)
const creatingOrg = ref(false)
const createOrgForm = ref<CreateOrganizationRequest>({ name: '', description: '' })

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
  // Load organizations to determine if CTA should be shown
  await loadOrganizations()
})

// Keep local value in sync with cached settings
watch(fontSize, (newSize) => {
  editorFontSize.value = newSize
})

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

// Create organization from Settings CTA
async function handleCreateOrganization() {
  if (!createOrgForm.value.name || createOrgForm.value.name.trim().length < 2) {
    toast.add({ severity: 'error', summary: 'Validation', detail: 'Organization name is required', life: 3000 })
    return
  }
  try {
    creatingOrg.value = true
    const org = await organizationsService.createOrganization(createOrgForm.value)
    toast.add({ severity: 'success', summary: 'Organization Created', detail: 'Redirecting to dashboard…', life: 2500 })
    showCreateOrgDialog.value = false
    createOrgForm.value = { name: '', description: '' }
    // Navigate to org dashboard
    router.push({ name: 'organization-dashboard', params: { id: org.id } })
  } catch (err) {
    console.error('Failed to create organization', err)
    toast.add({ severity: 'error', summary: 'Create Failed', detail: 'Could not create organization', life: 4000 })
  } finally {
    creatingOrg.value = false
    // Refresh orgs so sidebar can reveal Organizations menu
    await loadOrganizations()
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
              <Button
                label="Organizations"
                icon="pi pi-building"
                text
                :class="{ 'surface-hover': activeSection === 'organizations' }"
                class="w-full justify-content-start"
                @click="activeSection = 'organizations'"
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
        <!-- Organizations Section -->
        <Card v-if="activeSection === 'organizations'">
          <template #title>Organizations</template>
          <template #content>
            <div class="flex flex-column gap-4">
              <div v-if="hasOrganizations" class="flex align-items-center justify-content-between p-3 surface-ground border-round">
                <div>
                  <div class="font-medium">You're a member of {{ organizations.length }} organization(s).</div>
                  <div class="text-color-secondary text-sm">Manage collaboration, members, and app assignments.</div>
                </div>
                <div class="flex gap-2">
                  <Button label="Manage" icon="pi pi-users" outlined @click="router.push('/organizations')" />
                  <Button label="Create Organization" icon="pi pi-plus" @click="showCreateOrgDialog = true" />
                </div>
              </div>

              <div v-else class="p-3 surface-ground border-round">
                <div class="flex align-items-center justify-content-between">
                  <div>
                    <div class="font-medium">No organizations yet</div>
                    <div class="text-color-secondary text-sm">Create your first organization to enable team access and assignments.</div>
                  </div>
                  <Button label="Create Your First Organization" icon="pi pi-plus" @click="showCreateOrgDialog = true" />
                </div>
              </div>
            </div>
          </template>
        </Card>

        <!-- API Keys Section -->
        <ApiKeysSection v-if="activeSection === 'apikeys'" />

        <!-- AI Keys Section -->
        <AiKeysSection v-if="activeSection === 'aikeys'" />

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

    <!-- Create Organization Dialog (from Settings) -->
    <Dialog
      v-model:visible="showCreateOrgDialog"
      header="Create Organization"
      modal
      :style="{ width: '28rem' }"
    >
      <div class="flex flex-column gap-3 pt-2">
        <div class="flex flex-column gap-2">
          <label for="orgName" class="font-medium">Name *</label>
          <InputText id="orgName" v-model="createOrgForm.name" placeholder="Organization name" />
        </div>
        <div class="flex flex-column gap-2">
          <label for="orgDesc" class="font-medium">Description</label>
          <Textarea id="orgDesc" v-model="createOrgForm.description" placeholder="Optional description" rows="3" />
        </div>
        <small class="text-color-secondary">Slug is auto-generated. You can update settings later.</small>
      </div>
      <template #footer>
        <Button label="Cancel" text severity="secondary" :disabled="creatingOrg" @click="showCreateOrgDialog = false" />
        <Button label="Create" icon="pi pi-check" :loading="creatingOrg" :disabled="creatingOrg" @click="handleCreateOrganization" />
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
