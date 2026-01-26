<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { organizationsService } from '../services/organizationsService'
import { useToast } from 'primevue/usetoast'
import Card from 'primevue/card'
import Button from 'primevue/button'
import TabView from 'primevue/tabview'
import TabPanel from 'primevue/tabpanel'
import Tag from 'primevue/tag'
import type { OrganizationDto } from '../types'
import { OrganizationRole } from '../types'

const route = useRoute()
const router = useRouter()
const toast = useToast()

// State
const organization = ref<OrganizationDto | null>(null)
const loading = ref(true)

// Computed
const organizationId = computed(() => route.params.id as string)
const isOrgAdmin = computed(() => organization.value?.currentUserRole === OrganizationRole.OrgAdmin)
const isDeveloperOrAdmin = computed(() => 
  organization.value?.currentUserRole === OrganizationRole.OrgAdmin ||
  organization.value?.currentUserRole === OrganizationRole.Developer
)
const isAppUser = computed(() => organization.value?.currentUserRole === OrganizationRole.AppUser)

// Load organization on mount
onMounted(async () => {
  await loadOrganization()
})

// Load organization details
async function loadOrganization() {
  try {
    loading.value = true
    organization.value = await organizationsService.getOrganization(organizationId.value)
  } catch (error) {
    // Error is handled by the service
    router.push({ name: 'organizations' })
  } finally {
    loading.value = false
  }
}

// Navigate to specific section
function navigateToMembers() {
  router.push({ name: 'organization-members', params: { id: organizationId.value } })
}

function navigateToApps() {
  router.push({ name: 'organization-apps', params: { id: organizationId.value } })
}

function navigateToSettings() {
  router.push({ name: 'organization-settings', params: { id: organizationId.value } })
}

// Format date for display
const formatDate = (date: string) => {
  return new Date(date).toLocaleDateString('en-US', {
    year: 'numeric',
    month: 'long',
    day: 'numeric',
  })
}
</script>

<template>
  <div class="organization-dashboard">
    <div v-if="loading" class="loading-state">
      <i class="pi pi-spin pi-spinner" style="font-size: 2rem"></i>
      <p>Loading organization...</p>
    </div>

    <div v-else-if="organization">
      <!-- Header -->
      <div class="page-header">
        <div class="flex align-items-center gap-3">
          <Button
            icon="pi pi-arrow-left"
            text
            rounded
            @click="router.push({ name: 'organizations' })"
          />
          <div>
            <h1>{{ organization.name }}</h1>
            <p class="text-muted">{{ organization.description || 'No description' }}</p>
          </div>
        </div>
        <div class="flex gap-2">
          <Button
            v-if="isOrgAdmin"
            label="Settings"
            icon="pi pi-cog"
            outlined
            @click="navigateToSettings"
          />
        </div>
      </div>

      <!-- Overview Cards -->
      <div class="cards-grid">
        <!-- Members Card - Hidden for AppUser -->
        <Card v-if="!isAppUser">
          <template #title>
            <div class="flex align-items-center gap-2">
              <i class="pi pi-users" style="color: var(--p-primary-color)"></i>
              <span>Members</span>
            </div>
          </template>
          <template #content>
            <div class="stat-value">{{ organization.memberCount }}</div>
            <p class="text-muted">Active members</p>
          </template>
          <template #footer>
            <Button
              label="View Members"
              icon="pi pi-arrow-right"
              text
              @click="navigateToMembers"
            />
          </template>
        </Card>

        <!-- Projects Card - Hidden for AppUser -->
        <Card v-if="!isAppUser">
          <template #title>
            <div class="flex align-items-center gap-2">
              <i class="pi pi-folder" style="color: var(--p-primary-color)"></i>
              <span>Projects</span>
            </div>
          </template>
          <template #content>
            <div class="stat-value">{{ organization.projectCount }}</div>
            <p class="text-muted">Organization projects</p>
          </template>
          <template #footer>
            <Button
              label="View Projects"
              icon="pi pi-arrow-right"
              text
              @click="router.push({ name: 'projects' })"
            />
          </template>
        </Card>

        <!-- Apps Card - Label changes based on role -->
        <Card>
          <template #title>
            <div class="flex align-items-center gap-2">
              <i class="pi pi-th-large" style="color: var(--p-primary-color)"></i>
              <span>{{ isAppUser ? 'My Apps' : 'Apps' }}</span>
            </div>
          </template>
          <template #content>
            <div class="stat-value">-</div>
            <p class="text-muted">{{ isAppUser ? 'Apps assigned to you' : 'Available apps' }}</p>
          </template>
          <template #footer>
            <Button
              :label="isAppUser ? 'View My Apps' : 'Manage Apps'"
              icon="pi pi-arrow-right"
              text
              @click="isAppUser ? router.push({ name: 'assigned-apps' }) : navigateToApps()"
            />
          </template>
        </Card>
      </div>

      <!-- Organization Details -->
      <Card class="details-card">
        <template #title>Organization Details</template>
        <template #content>
          <div class="details-grid">
            <div class="detail-item">
              <span class="detail-label">Owner</span>
              <span class="detail-value">{{ organization.ownerName }}</span>
            </div>
            
            <div class="detail-item">
              <span class="detail-label">Your Role</span>
              <Tag :value="organization.currentUserRole || 'Member'" severity="info" />
            </div>
            
            <div class="detail-item">
              <span class="detail-label">Slug</span>
              <span class="detail-value">{{ organization.slug || 'Not set' }}</span>
            </div>
            
            <div class="detail-item">
              <span class="detail-label">Created</span>
              <span class="detail-value">{{ formatDate(organization.createdAt) }}</span>
            </div>
            
            <div class="detail-item">
              <span class="detail-label">Public Signup</span>
              <Tag 
                :value="organization.allowPublicSignup ? 'Enabled' : 'Disabled'"
                :severity="organization.allowPublicSignup ? 'success' : 'secondary'"
              />
            </div>
            
            <div v-if="organization.requireEmailDomain" class="detail-item">
              <span class="detail-label">Required Domain</span>
              <span class="detail-value">{{ organization.requireEmailDomain }}</span>
            </div>
          </div>
        </template>
      </Card>

      <!-- Quick Actions -->
      <Card v-if="isDeveloperOrAdmin" class="actions-card">
        <template #title>Quick Actions</template>
        <template #content>
          <div class="actions-grid">
            <Button
              label="Invite Members"
              icon="pi pi-user-plus"
              outlined
              @click="navigateToMembers"
            />
            <Button
              label="Manage Apps"
              icon="pi pi-th-large"
              outlined
              @click="navigateToApps"
            />
            <Button
              v-if="isOrgAdmin"
              label="Organization Settings"
              icon="pi pi-cog"
              outlined
              @click="navigateToSettings"
            />
          </div>
        </template>
      </Card>
    </div>
  </div>
</template>

<style scoped>
.organization-dashboard {
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

.loading-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 4rem 0;
  gap: 1rem;
}

.cards-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(250px, 1fr));
  gap: 1.5rem;
  margin-bottom: 2rem;
}

.stat-value {
  font-size: 2.5rem;
  font-weight: 700;
  color: var(--p-primary-color);
  margin-bottom: 0.5rem;
}

.details-card,
.actions-card {
  margin-bottom: 2rem;
}

.details-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
  gap: 1.5rem;
}

.detail-item {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.detail-label {
  font-size: 0.875rem;
  font-weight: 600;
  color: var(--p-text-muted-color);
  text-transform: uppercase;
  letter-spacing: 0.05em;
}

.detail-value {
  font-size: 1rem;
  font-weight: 500;
}

.actions-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
  gap: 1rem;
}
</style>
