<script setup lang="ts">
import { ref, onMounted } from 'vue'
import Card from 'primevue/card'
import Button from 'primevue/button'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Skeleton from 'primevue/skeleton'
import Tag from 'primevue/tag'
import { useToast } from 'primevue/usetoast'
import { organizationsService } from '../services'
import { useOrganizationRole } from '../composables/useOrganizationRole'
import type { PendingInvitationDto } from '../types'

const toast = useToast()
const { loadOrganizations } = useOrganizationRole()
const pendingInvitations = ref<PendingInvitationDto[]>([])
const loading = ref(false)
const processingId = ref<string | null>(null)

const loadPendingInvitations = async () => {
  try {
    loading.value = true
    const invitations = await organizationsService.getPendingInvitations()
    pendingInvitations.value = invitations
  } catch (error) {
    console.error('Error loading pending invitations:', error)
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: 'Failed to load pending invitations',
      life: 3000,
    })
  } finally {
    loading.value = false
  }
}

const acceptInvitation = async (token: string, orgName: string) => {
  try {
    processingId.value = token
    await organizationsService.acceptInvitation(token)
    await loadOrganizations()
    
    // Reload the list to ensure it's up to date
    await loadPendingInvitations()
    
    toast.add({
      severity: 'success',
      summary: 'Accepted',
      detail: `You've joined ${orgName}`,
      life: 3000,
    })
  } catch (error) {
    console.error('Error accepting invitation:', error)
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: 'Failed to accept invitation',
      life: 3000,
    })
  } finally {
    processingId.value = null
  }
}

const declineInvitation = async (invitationId: string, orgName: string) => {
  try {
    processingId.value = invitationId
    await organizationsService.declineInvitation(invitationId)
    await loadOrganizations()
    
    // Reload the list to ensure it's up to date
    await loadPendingInvitations()
    
    toast.add({
      severity: 'info',
      summary: 'Declined',
      detail: `Invitation from ${orgName} declined`,
      life: 3000,
    })
  } catch (error) {
    console.error('Error declining invitation:', error)
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: 'Failed to decline invitation',
      life: 3000,
    })
  } finally {
    processingId.value = null
  }
}

onMounted(() => {
  loadPendingInvitations()
})

const isExpired = (expiresAt: string) => {
  return new Date(expiresAt) < new Date()
}

const formatDate = (dateString: string) => {
  return new Date(dateString).toLocaleDateString('en-US', {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
  })
}
</script>

<template>
  <div v-if="pendingInvitations.length > 0" class="pending-invitations">
    <Card class="invitation-card">
      <template #header>
        <div class="card-header">
          <h3>🎉 Pending Invitations</h3>
          <span class="badge">{{ pendingInvitations.length }}</span>
        </div>
      </template>
      <template #content>
        <p class="description">
          You have been invited to join organizations. Accept or decline to manage your memberships.
        </p>

        <div v-if="loading" class="skeleton-container">
          <div v-for="i in 2" :key="i" class="skeleton-row">
            <Skeleton width="30%" height="2rem" />
            <Skeleton width="20%" height="2rem" />
            <Skeleton width="25%" height="2rem" />
          </div>
        </div>

        <div v-else class="invitations-table">
          <DataTable :value="pendingInvitations" responsive-layout="scroll">
            <Column field="organizationName" header="Organization">
              <template #body="{ data }">
                <div class="org-name">
                  {{ data.organizationName }}
                </div>
              </template>
            </Column>

            <Column field="role" header="Role">
              <template #body="{ data }">
                <Tag
                  :value="data.role"
                  :severity="
                    data.role === 'OrgAdmin'
                      ? 'danger'
                      : data.role === 'OrgMember'
                        ? 'info'
                        : 'warning'
                  "
                />
              </template>
            </Column>

            <Column field="expiresAt" header="Expires" class="expire-col">
              <template #body="{ data }">
                <span
                  :class="[
                    isExpired(data.expiresAt) ? 'text-danger' : 'text-secondary',
                  ]"
                >
                  {{ formatDate(data.expiresAt) }}
                </span>
              </template>
            </Column>

            <Column header="Actions" class="actions-col">
              <template #body="{ data }">
                <div class="action-buttons">
                  <Button
                    label="Accept"
                    icon="pi pi-check"
                    severity="success"
                    size="small"
                    :loading="processingId === data.token"
                    :disabled="processingId !== null || isExpired(data.expiresAt)"
                    @click="acceptInvitation(data.token, data.organizationName)"
                  />
                  <Button
                    label="Decline"
                    icon="pi pi-times"
                    severity="secondary"
                    size="small"
                    :loading="processingId === data.invitationId"
                    :disabled="processingId !== null"
                    @click="
                      declineInvitation(
                        data.invitationId,
                        data.organizationName
                      )
                    "
                  />
                </div>
              </template>
            </Column>
          </DataTable>
        </div>
      </template>
    </Card>
  </div>
</template>

<style scoped>
.pending-invitations {
  margin-bottom: 2rem;
}

.invitation-card {
  border: 1px solid #e5e7eb;
}

.card-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 1rem;
  background: linear-gradient(135deg, #f0f9ff 0%, #e0f2fe 100%);
  border-radius: 8px 8px 0 0;
}

.card-header h3 {
  margin: 0;
  font-size: 1.25rem;
  color: #0369a1;
}

.badge {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 1.5rem;
  height: 1.5rem;
  padding: 0 0.5rem;
  background: #0369a1;
  color: white;
  border-radius: 999px;
  font-weight: 600;
  font-size: 0.875rem;
}

.description {
  color: #666;
  margin: 1rem 0 1.5rem 0;
  font-size: 0.95rem;
}

.skeleton-container {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.skeleton-row {
  display: flex;
  gap: 1rem;
  align-items: center;
}

.invitations-table {
  overflow-x: auto;
}

.org-name {
  font-weight: 500;
  color: #1f2937;
}

.action-buttons {
  display: flex;
  gap: 0.5rem;
  justify-content: flex-end;
}

:deep(.p-datatable) {
  background: transparent;
  border: none;
}

:deep(.p-datatable .p-datatable-thead > tr > th) {
  background: #f9fafb;
  border-color: #e5e7eb;
  color: #374151;
  font-weight: 600;
}

:deep(.p-datatable .p-datatable-tbody > tr) {
  border-color: #e5e7eb;
}

:deep(.p-datatable .p-datatable-tbody > tr > td) {
  padding: 1rem;
  color: #6b7280;
}

.text-danger {
  color: #dc2626;
}

.text-secondary {
  color: #6b7280;
}
</style>
