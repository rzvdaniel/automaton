<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useToast } from 'primevue/usetoast'
import Button from 'primevue/button'
import Card from 'primevue/card'
import ProgressSpinner from 'primevue/progressspinner'
import Tag from 'primevue/tag'
import { useAuthStore } from '@/features/auth/stores/auth.store'
import { adminUsersService } from '../services/adminUsersService'
import type { AdminUser } from '../types/admin.types'

const route = useRoute()
const router = useRouter()
const toast = useToast()
const authStore = useAuthStore()

const isLoading = ref(false)
const user = ref<AdminUser | null>(null)

const userId = computed(() => String(route.params.userId || ''))
const isViewingSelf = computed(() => !!user.value && authStore.userId === user.value.id)
const isAlreadyAdmin = computed(() => {
  if (!user.value) {
    return false
  }

  return user.value.roles.some((role) => role === 'Admin' || role === 'SysAdmin')
})
const hasAccountActions = computed(() => {
  if (!user.value) {
    return false
  }

  return !user.value.emailConfirmed || !isViewingSelf.value || !isAlreadyAdmin.value
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

function navigateToUsage(to?: { path?: string; name?: string; params?: Record<string, string>; query?: Record<string, string> }) {
  if (!to) {
    return
  }

  void router.push(to)
}

function parseUtcDate(value?: string) {
  if (!value) {
    return null
  }

  const normalizedValue = /z$|[+-]\d{2}:\d{2}$/i.test(value) ? value : `${value}Z`
  const date = new Date(normalizedValue)

  if (Number.isNaN(date.getTime())) {
    return null
  }

  return date
}

function formatDate(value?: string) {
  const date = parseUtcDate(value)
  if (!date || date.getUTCFullYear() <= 1) {
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
    case 'google':
    case 'Google':
      return 'Google'
    case 'microsoft':
    case 'Microsoft':
      return 'Microsoft'
    case 'invitation':
      return 'Invitation'
    case 'adminCreated':
      return 'Admin'
    default:
      return value || 'Password'
  }
}

function statusSeverity(locked: boolean) {
  return locked ? 'danger' : 'success'
}

function verificationSeverity(confirmed: boolean) {
  return confirmed ? 'success' : 'warn'
}

function formatLastHeartbeat(value?: string) {
  const date = parseUtcDate(value)
  if (!date) {
    return 'Not tracked yet'
  }

  const diffSeconds = Math.max(0, Math.floor((Date.now() - date.getTime()) / 1000))
  if (diffSeconds < 60) {
    return `${diffSeconds} second${diffSeconds === 1 ? '' : 's'} ago`
  }

  const diffMinutes = Math.floor(diffSeconds / 60)
  if (diffMinutes < 60) {
    return `${diffMinutes} minute${diffMinutes === 1 ? '' : 's'} ago`
  }

  const diffHours = Math.floor(diffMinutes / 60)
  if (diffHours < 24) {
    return `${diffHours} hour${diffHours === 1 ? '' : 's'} ago`
  }

  return formatDate(value)
}

function triggerAction(actionLabel: string) {
  const userName = user.value?.name || 'this user'
  toast.add({
    severity: 'info',
    summary: 'Not implemented yet',
    detail: `${actionLabel} is not wired yet for ${userName}.`,
    life: 3000,
  })
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
              <dl class="detail-list detail-list--compact">
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
              <dl class="detail-list detail-list--compact">
                <div class="detail-item">
                  <dt>Email</dt>
                  <dd>
                    <Tag
                      :severity="verificationSeverity(user.emailConfirmed)"
                      :value="user.emailConfirmed ? 'Confirmed' : 'Pending'"
                    />
                  </dd>
                </div>
                <div class="detail-item">
                  <dt>Account</dt>
                  <dd>
                    <Tag
                      :severity="statusSeverity(user.isLocked)"
                      :value="user.isLocked ? 'Locked' : 'Active'"
                    />
                  </dd>
                </div>
                <div class="detail-item">
                  <dt>Failed Logins</dt>
                  <dd>{{ user.accessFailedCount ?? 0 }}</dd>
                </div>
                <div class="detail-item">
                  <dt>Provider</dt>
                  <dd>{{ formatProvider(user.registrationProvider) }}</dd>
                </div>
                <div class="detail-item">
                  <dt>Lockout</dt>
                  <dd>{{ user.isLocked ? 'Yes' : 'No' }}</dd>
                </div>
                <div class="detail-item">
                  <dt>MFA</dt>
                  <dd>Not Supported</dd>
                </div>
              </dl>
            </template>
          </Card>
        </div>

        <div class="col-12 lg:col-6">
          <Card class="detail-card detail-card--interactive h-full" @click="navigateToUsage({ name: 'admin-user-apps', params: { userId: user.id }, query: { userName: user.name } })">
            <template #title>Apps</template>
            <template #content>
              <div class="summary-card summary-card--compact">
                <div class="summary-stat-row">
                  <span class="summary-stat">{{ user.appCount.toLocaleString() }}</span>
                  <span class="summary-stat-label">total</span>
                </div>
                <div class="summary-meta">
                  {{ user.publishedAppCount.toLocaleString() }} published • {{ user.privateAppCount.toLocaleString() }} private
                </div>
                <div class="summary-detail">Last: {{ user.lastAppName || '—' }}</div>
              </div>
            </template>
          </Card>
        </div>

        <div class="col-12 lg:col-6">
          <Card class="detail-card detail-card--interactive h-full" @click="navigateToUsage({ name: 'admin-user-runners', params: { userId: user.id }, query: { userName: user.name } })">
            <template #title>Runners</template>
            <template #content>
              <div class="summary-card summary-card--compact">
                <div class="summary-stat-row">
                  <span class="summary-stat">{{ user.onlineRunnerCount.toLocaleString() }}</span>
                  <span class="summary-stat-label">online</span>
                </div>
                <div class="summary-meta">{{ user.runnerCount.toLocaleString() }} total runners</div>
                <div class="summary-detail">Heartbeat: {{ formatLastHeartbeat(user.lastRunnerHeartbeatAtUtc) }}</div>
              </div>
            </template>
          </Card>
        </div>

        <div v-if="hasAccountActions" class="col-12 lg:col-6">
          <Card class="detail-card h-full">
            <template #title>Actions</template>
            <template #content>
              <div class="actions-grid">
                <Button
                  label="Resend Confirmation"
                  icon="pi pi-envelope"
                  outlined
                  :disabled="user.emailConfirmed"
                  v-if="!user.emailConfirmed"
                  @click="triggerAction('Resend confirmation')"
                />
                <Button
                  v-if="!isViewingSelf"
                  :label="user.isLocked ? 'Unlock Account' : 'Disable Account'"
                  :icon="user.isLocked ? 'pi pi-lock-open' : 'pi pi-ban'"
                  :severity="user.isLocked ? 'success' : 'danger'"
                  outlined
                  @click="triggerAction(user.isLocked ? 'Unlock account' : 'Disable account')"
                />
                <Button
                  v-if="!isAlreadyAdmin"
                  label="Grant Admin"
                  icon="pi pi-shield"
                  outlined
                  @click="triggerAction('Grant admin')"
                />
              </div>
            </template>
          </Card>
        </div>

        <div class="col-12 lg:col-6">
          <Card class="detail-card h-full">
            <template #title>Activity</template>
            <template #content>
              <dl class="detail-list detail-list--compact">
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

.detail-list--compact {
  gap: 0.75rem;
}

.detail-item {
  display: grid;
  gap: 0.3rem;
}

.detail-list--compact .detail-item {
  grid-template-columns: minmax(110px, 140px) minmax(0, 1fr);
  align-items: start;
  column-gap: 1rem;
  row-gap: 0;
}

.detail-item dt {
  font-size: 0.875rem;
}

.detail-item dd {
  margin: 0;
  font-weight: 500;
}

.detail-list--compact .detail-item dd {
  word-break: break-word;
}

.actions-grid {
  display: grid;
  gap: 0.85rem;
}

.summary-card,
.status-item {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
  padding: 0.85rem 1rem;
  border: 1px solid var(--p-surface-200);
  border-radius: 0.9rem;
}

.summary-card {
  flex-direction: column;
  align-items: flex-start;
}

.summary-stat,
.summary-stat {
  font-size: 1.35rem;
  font-weight: 700;
}

.summary-detail,
.summary-meta {
  color: var(--p-text-muted-color, var(--p-text-color-secondary));
}

.summary-link {
  padding: 0;
  font-size: 1.1rem;
  font-weight: 600;
}

.summary-card--compact {
  gap: 0.45rem;
  padding: 0.9rem 1rem;
}

.summary-stat-row {
  display: flex;
  align-items: baseline;
  gap: 0.45rem;
}

.summary-stat-label {
  color: var(--p-text-muted-color, var(--p-text-color-secondary));
  font-size: 0.95rem;
  font-weight: 600;
}

.detail-card--interactive {
  cursor: pointer;
  transition: border-color 0.2s ease, transform 0.2s ease, box-shadow 0.2s ease;
}

.detail-card--interactive:hover {
  transform: translateY(-1px);
}

.detail-card--interactive:deep(.p-card) {
  height: 100%;
}

.detail-card--interactive:deep(.p-card-body) {
  height: 100%;
}

.detail-card--interactive:hover:deep(.p-card) {
  border-color: var(--primary-color);
  box-shadow: 0 10px 24px color-mix(in srgb, var(--primary-color) 10%, transparent);
}

@media (max-width: 768px) {
  .detail-list--compact .detail-item,
  .status-item {
    align-items: flex-start;
    flex-direction: column;
  }
}
</style>
