<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useToast } from 'primevue/usetoast'
import Button from 'primevue/button'
import Card from 'primevue/card'
import ProgressSpinner from 'primevue/progressspinner'
import Tag from 'primevue/tag'
import { adminUsersService } from '../services/adminUsersService'
import type { AdminUser } from '../types/admin.types'

const route = useRoute()
const router = useRouter()
const toast = useToast()

const isLoading = ref(false)
const user = ref<AdminUser | null>(null)

const userId = computed(() => String(route.params.userId || ''))

const usageItems = computed(() => {
  if (!user.value) {
    return []
  }

  return [
    { label: 'Apps', value: user.value.appCount.toLocaleString() },
    { label: 'Runners', value: user.value.runnerCount.toLocaleString() },
    { label: 'API Keys', value: 'Not tracked yet' },
    { label: 'AI Keys', value: 'Not tracked yet' },
  ]
})

async function loadUser() {
  if (!userId.value) {
    return
  }

  isLoading.value = true
  try {
    user.value = await adminUsersService.getUser(userId.value)
  } catch {
    toast.add({
      severity: 'error',
      summary: 'Error',
      detail: 'Failed to load user details',
      life: 3000,
    })
  } finally {
    isLoading.value = false
  }
}

function goBack() {
  void router.push({ name: 'admin-users' })
}

function formatDate(value?: string) {
  if (!value) {
    return '—'
  }

  const date = new Date(value)
  if (Number.isNaN(date.getTime()) || date.getUTCFullYear() <= 1) {
    return '—'
  }

  return date.toLocaleString()
}

function formatProvider(value?: string) {
  switch (value) {
    case 'password':
      return 'Password'
    case 'github':
      return 'GitHub'
    case 'invitation':
      return 'Invitation'
    case 'adminCreated':
      return 'Admin'
    default:
      return value || 'Unknown'
  }
}

function statusSeverity(locked: boolean) {
  return locked ? 'danger' : 'success'
}

function verificationSeverity(confirmed: boolean) {
  return confirmed ? 'success' : 'warn'
}

onMounted(() => {
  void loadUser()
})
</script>

<template>
  <div class="admin-user-detail-view">
    <div class="page-header">
      <div>
        <div class="breadcrumb-row">
          <Button
            label="Users"
            link
            class="breadcrumb-link"
            @click="goBack"
          />
          <span class="breadcrumb-separator">/</span>
          <span class="breadcrumb-current">{{ user?.name || 'User details' }}</span>
        </div>
        <h2 class="page-title">{{ user?.name || 'User Details' }}</h2>
        <p class="page-subtitle">Profile, security, usage, and activity for this account.</p>
      </div>

      <Button
        icon="pi pi-arrow-left"
        label="Back to Users"
        outlined
        @click="goBack"
      />
    </div>

    <div v-if="isLoading && !user" class="loading-state">
      <ProgressSpinner style="width: 40px; height: 40px" />
    </div>

    <template v-else-if="user">
      <div class="grid">
        <div class="col-12 lg:col-6">
          <Card class="detail-card h-full">
            <template #title>Profile</template>
            <template #content>
              <dl class="detail-list">
                <div class="detail-item">
                  <dt>Name</dt>
                  <dd>{{ user.name || '—' }}</dd>
                </div>
                <div class="detail-item">
                  <dt>Email</dt>
                  <dd>{{ user.email || '—' }}</dd>
                </div>
                <div class="detail-item">
                  <dt>Username</dt>
                  <dd>{{ user.publicUsername || '—' }}</dd>
                </div>
                <div class="detail-item">
                  <dt>Role</dt>
                  <dd>{{ user.roles.length ? user.roles.join(', ') : 'No roles' }}</dd>
                </div>
                <div class="detail-item">
                  <dt>Provider</dt>
                  <dd>{{ formatProvider(user.registrationProvider) }}</dd>
                </div>
                <div class="detail-item">
                  <dt>Registered</dt>
                  <dd>{{ formatDate(user.registeredAtUtc) }}</dd>
                </div>
                <div class="detail-item">
                  <dt>Last Login</dt>
                  <dd>{{ formatDate(user.lastLoginAtUtc) }}</dd>
                </div>
              </dl>
            </template>
          </Card>
        </div>

        <div class="col-12 lg:col-6">
          <Card class="detail-card h-full">
            <template #title>Security</template>
            <template #content>
              <div class="status-grid">
                <div class="status-item">
                  <span class="status-label">Email</span>
                  <Tag
                    :severity="verificationSeverity(user.emailConfirmed)"
                    :value="user.emailConfirmed ? 'Confirmed' : 'Pending'"
                  />
                </div>
                <div class="status-item">
                  <span class="status-label">Account</span>
                  <Tag
                    :severity="statusSeverity(user.isLocked)"
                    :value="user.isLocked ? 'Locked' : 'Active'"
                  />
                </div>
                <div class="status-item">
                  <span class="status-label">Failed logins</span>
                  <strong>{{ user.accessFailedCount ?? 0 }}</strong>
                </div>
              </div>
            </template>
          </Card>
        </div>

        <div class="col-12 lg:col-6">
          <Card class="detail-card h-full">
            <template #title>Usage</template>
            <template #content>
              <div class="usage-grid">
                <div
                  v-for="item in usageItems"
                  :key="item.label"
                  class="usage-card"
                >
                  <div class="usage-label">{{ item.label }}</div>
                  <div class="usage-value">{{ item.value }}</div>
                </div>
              </div>
            </template>
          </Card>
        </div>

        <div class="col-12 lg:col-6">
          <Card class="detail-card h-full">
            <template #title>Activity</template>
            <template #content>
              <dl class="detail-list">
                <div class="detail-item">
                  <dt>Last Login</dt>
                  <dd>{{ formatDate(user.lastLoginAtUtc) }}</dd>
                </div>
                <div class="detail-item">
                  <dt>Registered</dt>
                  <dd>{{ formatDate(user.registeredAtUtc) }}</dd>
                </div>
                <div class="detail-item">
                  <dt>Notes</dt>
                  <dd>Detailed login history and recent admin actions can be added next.</dd>
                </div>
              </dl>
            </template>
          </Card>
        </div>
      </div>
    </template>
  </div>
</template>

<style scoped>
.admin-user-detail-view {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.page-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 1rem;
  flex-wrap: wrap;
}

.breadcrumb-row {
  display: flex;
  align-items: center;
  gap: 0.35rem;
  margin-bottom: 0.5rem;
}

.breadcrumb-link {
  padding: 0;
}

.breadcrumb-separator,
.breadcrumb-current,
.page-subtitle,
.detail-list dt,
.usage-label,
.status-label {
  color: var(--p-text-muted-color, var(--p-text-color-secondary));
}

.page-title {
  margin: 0;
  font-size: 1.75rem;
  font-weight: 700;
}

.page-subtitle {
  margin: 0.35rem 0 0;
}

.loading-state {
  display: flex;
  justify-content: center;
  padding: 2rem 0;
}

.detail-list {
  display: grid;
  gap: 1rem;
  margin: 0;
}

.detail-item {
  display: grid;
  gap: 0.3rem;
}

.detail-item dt {
  font-size: 0.875rem;
}

.detail-item dd {
  margin: 0;
  font-weight: 500;
}

.status-grid,
.usage-grid {
  display: grid;
  gap: 0.85rem;
}

.status-item,
.usage-card {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  padding: 0.85rem 1rem;
  border: 1px solid var(--p-surface-200);
  border-radius: 0.9rem;
}

.usage-card {
  flex-direction: column;
  align-items: flex-start;
}

.usage-value {
  font-size: 1.35rem;
  font-weight: 700;
}

@media (max-width: 768px) {
  .status-item {
    align-items: flex-start;
    flex-direction: column;
  }
}
</style>