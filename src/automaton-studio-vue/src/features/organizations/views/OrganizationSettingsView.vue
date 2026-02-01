<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { organizationsService } from '../services/organizationsService'
import { useToast } from 'primevue/usetoast'
import Button from 'primevue/button'
import Card from 'primevue/card'
import InputText from 'primevue/inputtext'
import Textarea from 'primevue/textarea'
import InputSwitch from 'primevue/inputswitch'
import type { OrganizationDto, UpdateOrganizationRequest } from '../types'
import { OrganizationRole } from '../types'

const route = useRoute()
const router = useRouter()
const toast = useToast()

// State
const organization = ref<OrganizationDto | null>(null)
const loading = ref(true)
const saving = ref(false)

// Computed
const organizationId = computed(() => route.params.id as string)
const isOrgAdmin = computed(() => organization.value?.currentUserRole === OrganizationRole.OrgAdmin)

// Form
const settingsForm = ref<UpdateOrganizationRequest>({
  name: '',
  description: '',
  allowPublicSignup: false,
  requireEmailDomain: '',
  logoUrl: '',
  primaryColor: '',
})

// Load organization on mount
onMounted(async () => {
  await loadOrganization()
  
  // Redirect non-OrgAdmin users
  if (organization.value && !isOrgAdmin.value) {
    toast.add({
      severity: 'warn',
      summary: 'Access Restricted',
      detail: 'Only OrgAdmins can access organization settings',
      life: 3000,
    })
    router.push({ name: 'organization-dashboard', params: { id: organizationId.value } })
  }
})

// Load organization details
async function loadOrganization() {
  try {
    loading.value = true
    organization.value = await organizationsService.getOrganization(organizationId.value)
    
    // Populate form
    if (organization.value) {
      settingsForm.value = {
        name: organization.value.name,
        description: organization.value.description || '',
        allowPublicSignup: organization.value.allowPublicSignup,
        requireEmailDomain: organization.value.requireEmailDomain || '',
        logoUrl: organization.value.logoUrl || '',
        primaryColor: organization.value.primaryColor || '',
      }
    }
  } catch (error) {
    router.push({ name: 'organization-dashboard', params: { id: organizationId.value } })
  } finally {
    loading.value = false
  }
}

// Handle save settings
async function handleSaveSettings() {
  if (!settingsForm.value.name) {
    toast.add({
      severity: 'error',
      summary: 'Validation Error',
      detail: 'Organization name is required',
      life: 3000,
    })
    return
  }

  try {
    saving.value = true
    await organizationsService.updateOrganization(organizationId.value, settingsForm.value)
    await loadOrganization()
  } catch (error) {
    // Error toast is already shown by service
  } finally {
    saving.value = false
  }
}

function navigateBack() {
  router.push({ name: 'organization-dashboard', params: { id: organizationId.value } })
}
</script>

<template>
  <div class="organization-settings">
    <div class="page-header">
      <div class="flex align-items-center gap-3">
        <Button
          icon="pi pi-arrow-left"
          text
          rounded
          @click="navigateBack"
        />
        <div>
          <h1>Organization Settings</h1>
          <p class="text-muted">Manage organization details and preferences</p>
        </div>
      </div>
      <Button
        label="Save Changes"
        icon="pi pi-save"
        :loading="saving"
        @click="handleSaveSettings"
      />
    </div>

    <div v-if="loading" class="loading-state">
      <i class="pi pi-spin pi-spinner" style="font-size: 2rem"></i>
      <p>Loading settings...</p>
    </div>

    <div v-else class="settings-container">
      <Card>
        <template #title>General Information</template>
        <template #content>
          <div class="flex flex-column gap-3">
            <div class="flex flex-column gap-2">
              <label for="org-name" class="font-semibold">Organization Name *</label>
              <InputText
                id="org-name"
                v-model="settingsForm.name"
                placeholder="Enter organization name"
              />
            </div>

            <div class="flex flex-column gap-2">
              <label for="org-description" class="font-semibold">Description</label>
              <Textarea
                id="org-description"
                v-model="settingsForm.description"
                rows="3"
                placeholder="Enter organization description"
              />
            </div>

            <div class="flex flex-column gap-2">
              <label for="org-slug" class="font-semibold">Slug (Read-only)</label>
              <InputText
                id="org-slug"
                :modelValue="organization?.slug"
                placeholder="Slug is auto-generated"
                disabled
              />
              <small class="text-muted">URL-friendly identifier for your organization</small>
            </div>
          </div>
        </template>
      </Card>

      <Card class="mt-3">
        <template #title>Access Control</template>
        <template #content>
          <div class="flex flex-column gap-3">
            <div class="flex align-items-center gap-3">
              <InputSwitch v-model="settingsForm.allowPublicSignup" />
              <div>
                <label class="font-semibold">Allow Public Signup</label>
                <p class="text-muted text-sm">Anyone with matching email domain can join</p>
              </div>
            </div>

            <div class="flex flex-column gap-2">
              <label for="email-domain" class="font-semibold">Required Email Domain</label>
              <InputText
                id="email-domain"
                v-model="settingsForm.requireEmailDomain"
                placeholder="@example.com"
              />
              <small class="text-muted">Restrict invitations to specific email domain</small>
            </div>
          </div>
        </template>
      </Card>

      <Card class="mt-3">
        <template #title>Branding</template>
        <template #content>
          <div class="flex flex-column gap-3">
            <div class="flex flex-column gap-2">
              <label for="logo-url" class="font-semibold">Logo URL</label>
              <InputText
                id="logo-url"
                v-model="settingsForm.logoUrl"
                placeholder="https://example.com/logo.png"
              />
            </div>

            <div class="flex flex-column gap-2">
              <label for="primary-color" class="font-semibold">Primary Color</label>
              <InputText
                id="primary-color"
                v-model="settingsForm.primaryColor"
                placeholder="#1976D2"
              />
              <small class="text-muted">Hex color code for branding (e.g., #1976D2)</small>
            </div>
          </div>
        </template>
      </Card>
    </div>
  </div>
</template>

<style scoped>
.organization-settings {
  padding: 2rem;
}

.page-header {
  display: flex;
  justify-content: space-between;
  align-items: start;
  margin-bottom: 2rem;
}

.page-header h1 {
  margin: 0 0 0.5rem 0;
  font-size: 2rem;
  font-weight: 600;
}

.text-muted {
  color: var(--p-text-muted-color);
}

.text-sm {
  font-size: 0.875rem;
}

.loading-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 4rem 0;
  gap: 1rem;
}

.settings-container {
  max-width: 800px;
}
</style>
