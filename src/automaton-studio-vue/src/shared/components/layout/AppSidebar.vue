<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { watch } from 'vue'
import Button from 'primevue/button'
import ProgressBar from 'primevue/progressbar'
import { useOrganizationRole } from '@/features/organizations/composables/useOrganizationRole'
import { useAuthStore } from '@/features/auth/stores/auth.store'
import billingService, { type BillingStatusResponse } from '@/features/settings/services/billingService'

const router = useRouter()
const route = useRoute()
const isExpanded = ref(false)
const authStore = useAuthStore()

// Load organization roles for access control
const { isAppUserOnly, loadOrganizations, loading } = useOrganizationRole()

// Refresh organizations on route changes to reveal menu after creation
watch(() => route.fullPath, async () => {
  await loadOrganizations()
})

interface NavItem {
  label: string
  icon: string
  to: string
  divider?: boolean
  hideForAppUser?: boolean
}

const navItems: NavItem[] = [
  { label: 'Dashboard', icon: 'pi pi-home', to: '/' },
  // { label: 'Flows', icon: 'pi pi-bolt', to: '/flows' },
  { label: 'Apps', icon: 'pi pi-globe', to: '/apps', hideForAppUser: true },
  // { label: 'Steps', icon: 'pi pi-code', to: '/steps' },
  { label: 'Runners', icon: 'pi pi-desktop', to: '/runners', hideForAppUser: true },
  { label: 'Projects', icon: 'pi pi-building', to: '/projects', hideForAppUser: true },
  { label: 'Organizations', icon: 'pi pi-users', to: '/organizations' },
  { label: 'My Apps', icon: 'pi pi-th-large', to: '/my-apps', hideForAppUser: false },
  { label: 'Admin', icon: 'pi pi-shield', to: '/admin', hideForAppUser: true },
]

const bottomNavItems: NavItem[] = [
  { label: 'Downloads', icon: 'pi pi-download', to: '/downloads', hideForAppUser: true },
  { label: 'Billing', icon: 'pi pi-credit-card', to: '/billing' },
  { label: 'Settings', icon: 'pi pi-cog', to: '/settings' },
  { label: 'Documentation', icon: 'pi pi-book', to: '/docs' },
]

// Billing usage widget
const billingStatus = ref<BillingStatusResponse | null>(null)

async function loadBillingStatus() {
  try {
    billingStatus.value = await billingService.getBillingStatus()
  } catch {
    // silently ignore — widget just won't show
  }
}

onMounted(async () => {
  // Load organizations to determine user's roles
  await loadOrganizations()
  await loadBillingStatus()
})

const usagePercent = computed(() => billingStatus.value?.percentUsed ?? 0)
const usageSeverity = computed(() => {
  const pct = usagePercent.value
  if (pct >= 90) return 'danger'
  if (pct >= 70) return 'warn'
  return null
})
const isAdmin = computed(() => {
  const roles = authStore.user?.roles
  if (!roles) return false
  return roles.includes('Admin') || roles.includes('SysAdmin')
})

// Filter navigation items based on user role
const visibleNavItems = computed(() => {
  let base = !isAppUserOnly.value
    ? navItems.filter(item => item.to !== '/my-apps')
    : navItems.filter(item => !item.hideForAppUser)
  // Show Admin only for Admin/SysAdmin roles
  if (!isAdmin.value) {
    base = base.filter(item => item.to !== '/admin')
  }
  return base
})

const visibleBottomNavItems = computed(() => {
  if (isAppUserOnly.value) {
    return bottomNavItems.filter(item => !item.hideForAppUser)
  }
  return bottomNavItems
})

const isActive = (to: string) => {
  if (to === '/') {
    return route.path === '/'
  }
  return route.path.startsWith(to)
}

const navigate = (to: string) => {
  router.push(to)
}

const sidebarClass = computed(() => ({
  'app-sidebar': true,
  expanded: isExpanded.value,
}))
</script>

<template>
  <aside :class="sidebarClass" @mouseenter="isExpanded = true" @mouseleave="isExpanded = false">
    <div class="sidebar-content">
      <!-- Logo -->
      <div class="sidebar-logo">
        <i class="pi pi-bolt text-2xl" style="color: var(--p-primary-500)"></i>
        <span v-if="isExpanded" class="logo-text">Automaton</span>
      </div>

      <!-- Main Navigation -->
      <nav class="sidebar-nav">
        <div v-if="loading" class="nav-loading">
          <span v-if="isExpanded">Loading menu...</span>
        </div>
        <div v-for="item in visibleNavItems" v-else :key="item.to" class="nav-item-wrapper">
          <Button
            :title="isExpanded ? undefined : item.label"
            :class="['nav-item', { active: isActive(item.to) }]"
            :severity="isActive(item.to) ? 'primary' : 'secondary'"
            :text="!isActive(item.to)"
            rounded
            @click="navigate(item.to)"
          >
            <i :class="item.icon"></i>
            <span v-if="isExpanded" class="nav-label">{{ item.label }}</span>
          </Button>
        </div>
      </nav>

      <!-- Spacer -->
      <div class="sidebar-spacer"></div>

      <!-- Billing usage widget (expanded only) -->
      <div v-if="isExpanded && billingStatus" class="billing-widget" @click="navigate('/billing')">
        <div class="billing-widget-header">
          <span class="billing-plan">{{ billingStatus.planName }}</span>
          <span
            class="billing-pct"
            :class="{
              'text-red-400': usageSeverity === 'danger',
              'text-yellow-400': usageSeverity === 'warn',
            }"
          >{{ usagePercent }}%</span>
        </div>
        <ProgressBar
          :value="usagePercent"
          :showValue="false"
          style="height: 4px"
          :class="{
            'progress-warn': usageSeverity === 'warn',
            'progress-danger': usageSeverity === 'danger',
          }"
        />
        <div class="billing-widget-footer">
          <span class="text-color-secondary">${{ billingStatus.currentMonthCostUsd.toFixed(2) }} / ${{ billingStatus.monthlyCostLimitUsd.toFixed(2) }}</span>
          <span v-if="!billingStatus.hasActiveSubscription" class="billing-upgrade" @click.stop="navigate('/pricing')">Upgrade</span>
        </div>
      </div>

      <!-- Bottom Navigation -->
      <nav class="sidebar-nav sidebar-nav-bottom">
        <div class="nav-divider"></div>
        <div v-for="item in visibleBottomNavItems" v-if="!loading" :key="item.to" class="nav-item-wrapper">
          <Button
            :title="isExpanded ? undefined : item.label"
            :class="['nav-item', { active: isActive(item.to) }]"
            :severity="isActive(item.to) ? 'primary' : 'secondary'"
            :text="!isActive(item.to)"
            rounded
            @click="navigate(item.to)"
          >
            <i :class="item.icon"></i>
            <span v-if="isExpanded" class="nav-label">{{ item.label }}</span>
          </Button>
        </div>
      </nav>
    </div>
  </aside>
</template>

<style scoped>
/* Base sidebar styles - dark mode handled in main.css */
.app-sidebar {
  position: fixed;
  top: 0;
  left: 0;
  height: 100vh;
  overflow: hidden;
  z-index: 1000;
}

.sidebar-content {
  display: flex;
  flex-direction: column;
  height: 100%;
  padding: 1rem 0.75rem;
}

.sidebar-logo {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  padding: 0.5rem;
  margin-bottom: 1.5rem;
}

.logo-text {
  font-weight: 700;
  font-size: 1.125rem;
  white-space: nowrap;
}

.sidebar-nav {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.sidebar-spacer {
  flex: 1;
}

.nav-item-wrapper {
  width: 100%;
}

.nav-item {
  width: 100%;
  justify-content: flex-start;
  gap: 0.75rem;
  padding: 0.75rem;
  border-radius: 0.5rem;
  transition: all 0.15s ease;
}

.nav-item i {
  font-size: 1.125rem;
  min-width: 1.25rem;
}

.nav-label {
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.nav-loading {
  padding: 0.5rem;
  color: var(--p-text-muted-color);
  font-size: 0.875rem;
}

.nav-divider {
  height: 1px;
  background-color: var(--p-surface-200);
  margin: 0.5rem 0;
}

.sidebar-nav-bottom {
  margin-top: auto;
}

/* Billing usage widget */
.billing-widget {
  margin: 0 0 0.5rem;
  padding: 0.6rem 0.75rem;
  border-radius: 0.5rem;
  background: var(--p-surface-100);
  cursor: pointer;
  transition: background 0.15s ease;
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
}

.billing-widget:hover {
  background: var(--p-surface-200);
}

.billing-widget-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.billing-plan {
  font-size: 0.75rem;
  font-weight: 600;
  white-space: nowrap;
}

.billing-pct {
  font-size: 0.7rem;
  font-weight: 500;
}

.billing-widget-footer {
  display: flex;
  justify-content: space-between;
  align-items: center;
  font-size: 0.7rem;
}

.billing-upgrade {
  color: var(--p-primary-500);
  font-weight: 600;
  cursor: pointer;
}

.billing-upgrade:hover {
  text-decoration: underline;
}

:deep(.progress-warn .p-progressbar-value) {
  background: var(--p-yellow-400);
}

:deep(.progress-danger .p-progressbar-value) {
  background: var(--p-red-400);
}
</style>
