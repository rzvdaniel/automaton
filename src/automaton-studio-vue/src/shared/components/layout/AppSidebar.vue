<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { watch } from 'vue'
import Button from 'primevue/button'
import { useOrganizationRole } from '@/features/organizations/composables/useOrganizationRole'
import { useAuthStore } from '@/features/auth/stores/auth.store'
import billingService, { type BillingStatusResponse } from '@/features/settings/services/billingService'
import { apiKeysService } from '@/features/settings/services/apikeys.service'
import { usePaidPlansAvailable } from '@/shared/composables/usePaidPlansAvailable'
import { useSidebarState } from '@/shared/composables/useSidebarState'

const publicDocsUrl = 'https://automaton.monster/docs'

const router = useRouter()
const route = useRoute()
const authStore = useAuthStore()
const { hasPaidPlans, ensureLoaded: ensurePaidPlansLoaded } = usePaidPlansAvailable()
const { isExpanded, isPinned, isMobile, isMobileOpen, setHovered, togglePinned, closeMobile } = useSidebarState()

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
  external?: boolean
  hideForAppUser?: boolean
  accent?: 'green'
  badge?: string | null
}

interface NavSection {
  key: string
  items: NavItem[]
}

const mainNavItems: NavItem[] = [
  { label: 'Dashboard', icon: 'pi pi-home', to: '/' },
  { label: 'Apps', icon: 'pi pi-globe', to: '/apps', hideForAppUser: true },
  { label: 'Runners', icon: 'pi pi-desktop', to: '/runners', hideForAppUser: true },
  { label: 'Projects', icon: 'pi pi-building', to: '/projects', hideForAppUser: true },
]

const collaborationNavItems: NavItem[] = [
  { label: 'Organizations', icon: 'pi pi-users', to: '/organizations' },
  { label: 'My Apps', icon: 'pi pi-th-large', to: '/my-apps', hideForAppUser: false },
]

const adminNavItems: NavItem[] = [
  { label: 'Admin', icon: 'pi pi-shield', to: '/admin', hideForAppUser: true },
]

const utilityNavItems: NavItem[] = [
  { label: 'CLI & SDKs', icon: 'pi pi-download', to: '/downloads', hideForAppUser: true },
  { label: 'Billing', icon: 'pi pi-credit-card', to: '/billing' },
  { label: 'Settings', icon: 'pi pi-cog', to: '/settings' },
  { label: 'Documentation', icon: 'pi pi-book', to: publicDocsUrl, external: true },
]

// Billing usage widget
const billingStatus = ref<BillingStatusResponse | null>(null)
const apiKeysCount = ref(0)

async function loadBillingStatus() {
  try {
    billingStatus.value = await billingService.getBillingStatus()
  } catch {
    // silently ignore — widget just won't show
  }
}

async function loadApiKeysCount() {
  try {
    const keys = await apiKeysService.list()
    apiKeysCount.value = keys.length
  } catch {
    apiKeysCount.value = 0
  }
}

onMounted(async () => {
  // Load organizations to determine user's roles
  await loadOrganizations()
  await Promise.all([
    loadBillingStatus(),
    loadApiKeysCount(),
    ensurePaidPlansLoaded(),
  ])
})

const sidebarPlansLabel = computed(() => hasPaidPlans.value ? 'Upgrade' : 'Plans soon')
const runnerSummary = computed(() => {
  const runner = billingStatus.value?.capabilities.hostedRunner
  if (!runner?.enabled) return 'Self-hosted runner'
  return `${runner.cpuCores} vCPU · ${runner.memoryMb} MB`
})
const isAdmin = computed(() => {
  const roles = authStore.user?.roles
  if (!roles) return false
  return roles.includes('Admin') || roles.includes('SysAdmin')
})

const apiKeysBadge = computed(() => {
  if (apiKeysCount.value <= 0) return 'Setup'
  return apiKeysCount.value > 9 ? '9+' : String(apiKeysCount.value)
})

const apiKeysNavItem = computed<NavItem>(() => ({
  label: 'API Keys',
  icon: 'pi pi-key',
  to: '/apikeys',
  hideForAppUser: true,
  accent: 'green',
  badge: apiKeysBadge.value,
}))

const primaryNavItems = computed<NavItem[]>(() => [
  mainNavItems[0],
  mainNavItems[1],
  mainNavItems[2],
  apiKeysNavItem.value,
  mainNavItems[3],
])

function filterItems(items: NavItem[]) {
  let filtered = items

  if (isAppUserOnly.value) {
    filtered = filtered.filter((item) => !item.hideForAppUser)
  } else {
    filtered = filtered.filter((item) => item.to !== '/my-apps')
  }

  if (!isAdmin.value) {
    filtered = filtered.filter((item) => item.to !== '/admin')
  }

  return filtered
}

const visibleSections = computed<NavSection[]>(() => {
  const sections: NavSection[] = [
    { key: 'main', items: filterItems(primaryNavItems.value) },
    { key: 'collaboration', items: filterItems(collaborationNavItems) },
    { key: 'admin', items: filterItems(adminNavItems) },
  ]

  return sections.filter((section) => section.items.length > 0)
})

const visibleBottomNavItems = computed(() => filterItems(utilityNavItems))

const hasTopDividerAfter = (index: number) => {
  return index < visibleSections.value.length - 1
}

const sidebarClass = computed(() => ({
  'app-sidebar': true,
  expanded: isExpanded.value,
  pinned: isPinned.value,
  mobile: isMobile.value,
  'mobile-open': isMobileOpen.value,
}))

const shellBackdropClass = computed(() => ({
  'sidebar-backdrop': true,
  visible: isMobileOpen.value && isMobile.value,
}))

const tooltipOptions = (label: string) =>
  !isExpanded.value && !isMobile.value
    ? { value: label, showDelay: 0, hideDelay: 0, pt: { text: { style: 'white-space: nowrap;' } } }
    : undefined

const navigate = (item: NavItem) => {
  if (item.external) {
    window.open(item.to, '_blank', 'noopener')
  } else {
    router.push(item.to)
  }

  if (isMobile.value) {
    closeMobile()
  }
}

const isActive = (to: string) => {
  if (to.startsWith('http')) {
    return false
  }
  if (to === '/') {
    return route.path === '/'
  }
  return route.path.startsWith(to)
}

</script>

<template>
  <div :class="shellBackdropClass" @click="closeMobile"></div>
  <aside :class="sidebarClass" @mouseenter="setHovered(true)" @mouseleave="setHovered(false)">
    <div class="sidebar-content">
      <div class="sidebar-logo">
        <div class="sidebar-brand">
          <img src="/logo.svg" alt="Automaton" class="sidebar-brand-logo" />
          <span v-if="isExpanded" class="logo-text">Automaton</span>
        </div>
        <Button
          v-if="isExpanded && !isMobile"
          v-tooltip.left="isPinned ? 'Unpin sidebar' : 'Pin sidebar'"
          :icon="isPinned ? 'pi pi-lock' : 'pi pi-thumbtack'"
          text
          rounded
          severity="secondary"
          class="sidebar-pin"
          @click="togglePinned"
        />
      </div>

      <nav class="sidebar-nav">
        <div v-if="loading" class="nav-loading">
          <span v-if="isExpanded">Loading menu...</span>
        </div>
        <template v-else>
          <div v-for="(section, sectionIndex) in visibleSections" :key="section.key" class="nav-section">
            <div v-for="item in section.items" :key="item.to" class="nav-item-wrapper">
              <Button
                v-tooltip.right="tooltipOptions(item.label)"
                :class="[
                  'nav-item',
                  {
                    active: isActive(item.to),
                    'nav-item-accent-green': item.accent === 'green' && !isActive(item.to),
                    'nav-item-accent-green-active': item.accent === 'green' && isActive(item.to),
                  },
                ]"
                :severity="isActive(item.to) ? 'primary' : 'secondary'"
                :text="!isActive(item.to)"
                rounded
                @click="navigate(item)"
              >
                <span
                  class="nav-icon-wrap"
                  :class="{
                    'nav-icon-wrap-green': item.accent === 'green',
                    'nav-icon-wrap-green-active': item.accent === 'green' && isActive(item.to),
                  }"
                >
                  <i :class="item.icon"></i>
                </span>
                <span v-if="isExpanded" class="nav-label">{{ item.label }}</span>
                <span
                  v-if="isExpanded && item.badge"
                  class="nav-badge"
                  :class="{ 'nav-badge-green': item.accent === 'green' }"
                >
                  {{ item.badge }}
                </span>
              </Button>
            </div>
            <div v-if="hasTopDividerAfter(sectionIndex)" class="nav-divider"></div>
          </div>
        </template>
      </nav>

      <div class="sidebar-spacer"></div>

      <div v-if="isExpanded && billingStatus" class="billing-widget" @click="navigate({ label: 'Billing', icon: 'pi pi-credit-card', to: '/billing' })">
        <div class="billing-widget-header">
          <span class="billing-plan">{{ billingStatus.planName }}</span>
          <span v-if="billingStatus.hasActiveSubscription" class="billing-status">Active</span>
        </div>
        <div class="billing-widget-footer">
          <span class="text-color-secondary">{{ runnerSummary }}</span>
          <span v-if="!billingStatus.hasActiveSubscription" class="billing-upgrade" @click.stop="navigate({ label: 'Pricing', icon: 'pi pi-credit-card', to: '/pricing' })">{{ sidebarPlansLabel }}</span>
        </div>
      </div>

      <nav class="sidebar-nav sidebar-nav-bottom">
        <div class="nav-divider"></div>
        <div v-for="item in visibleBottomNavItems" v-if="!loading" :key="item.to" class="nav-item-wrapper">
          <Button
            v-tooltip.right="tooltipOptions(item.label)"
            :class="[
              'nav-item',
              {
                active: isActive(item.to),
                'nav-item-accent-green': item.accent === 'green' && !isActive(item.to),
                'nav-item-accent-green-active': item.accent === 'green' && isActive(item.to),
              },
            ]"
            :severity="isActive(item.to) ? 'primary' : 'secondary'"
            :text="!isActive(item.to)"
            rounded
            @click="navigate(item)"
          >
            <span
              class="nav-icon-wrap"
              :class="{
                'nav-icon-wrap-green': item.accent === 'green',
                'nav-icon-wrap-green-active': item.accent === 'green' && isActive(item.to),
              }"
            >
              <i :class="item.icon"></i>
            </span>
            <span v-if="isExpanded" class="nav-label">{{ item.label }}</span>
            <span
              v-if="isExpanded && item.badge"
              class="nav-badge"
              :class="{ 'nav-badge-green': item.accent === 'green' }"
            >
              {{ item.badge }}
            </span>
          </Button>
        </div>
      </nav>
    </div>
  </aside>
</template>

<style scoped>
.app-sidebar {
  position: fixed;
  top: 0;
  left: 0;
  height: 100vh;
  overflow: hidden;
  z-index: 1000;
}

.sidebar-backdrop {
  position: fixed;
  inset: 0;
  background: rgba(15, 23, 42, 0.48);
  opacity: 0;
  pointer-events: none;
  transition: opacity 0.18s ease;
  z-index: 998;
}

.sidebar-backdrop.visible {
  opacity: 1;
  pointer-events: auto;
}

.sidebar-content {
  display: flex;
  flex-direction: column;
  height: 100%;
  padding: 0.75rem 0.625rem;
  overflow-y: auto;
  overscroll-behavior: contain;
  scrollbar-gutter: stable;
}

.sidebar-logo {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0.35rem 0.45rem;
  margin-bottom: 0.9rem;
}

.sidebar-brand {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}

.sidebar-brand-logo {
  width: 2rem;
  height: 2rem;
  flex: 0 0 2rem;
  display: block;
}

.logo-text {
  font-weight: 700;
  font-size: 1.125rem;
  white-space: nowrap;
}

.sidebar-pin {
  flex-shrink: 0;
}

.sidebar-nav {
  display: flex;
  flex-direction: column;
  gap: 0.32rem;
}

.nav-badge {
  margin-left: auto;
  font-size: 0.64rem;
  font-weight: 700;
  padding: 0.14rem 0.42rem;
  border-radius: 999px;
  background: var(--p-surface-200);
  color: var(--p-text-muted-color);
}

.nav-badge-green {
  background: #dcfce7;
  color: #047857;
}

.nav-section {
  display: flex;
  flex-direction: column;
  gap: 0.2rem;
}

.sidebar-spacer {
  flex: 1;
  min-height: 0.5rem;
}

.nav-item-wrapper {
  width: 100%;
}

.nav-item {
  width: 100%;
  justify-content: flex-start;
  gap: 0.6rem;
  padding: 0.58rem 0.68rem;
  border-radius: 0.7rem;
  transition: all 0.15s ease;
  position: relative;
}

.nav-item i {
  font-size: 1.125rem;
  min-width: 1.25rem;
}

.nav-icon-wrap {
  width: 1.8rem;
  height: 1.8rem;
  border-radius: 0.58rem;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}

.nav-icon-wrap-green {
  background: #dcfce7;
  color: #047857;
}

.nav-icon-wrap-green-active {
  background: rgba(255, 255, 255, 0.2);
  color: currentColor;
}

.nav-item.active::before {
  content: '';
  position: absolute;
  left: 0.35rem;
  top: 0.5rem;
  bottom: 0.5rem;
  width: 3px;
  border-radius: 999px;
  background: var(--p-primary-500);
}

.nav-label {
  font-size: 0.93rem;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.nav-loading {
  padding: 0.5rem;
  color: var(--p-text-muted-color);
  font-size: 0.875rem;
}

.nav-item-accent-green {
  background: linear-gradient(135deg, #16a34a, #059669);
  border: 1px solid #22c55e;
  color: #f0fdf4;
  box-shadow: 0 10px 22px rgba(22, 163, 74, 0.22);
}

.nav-item-accent-green:hover {
  background: linear-gradient(135deg, #15803d, #047857);
  border-color: #34d399;
  color: #ffffff;
}

.nav-item-accent-green-active {
  background: linear-gradient(135deg, #15803d, #059669);
}

.nav-item-accent-green :deep(.p-button-label),
.nav-item-accent-green-active :deep(.p-button-label) {
  color: inherit;
}

.nav-divider {
  height: 1px;
  background-color: var(--p-surface-200);
  margin: 0.45rem 0;
}

.sidebar-nav-bottom {
  margin-top: auto;
}

/* Billing usage widget */
.billing-widget {
  margin: 0 0 0.5rem;
  padding: 0.5rem 0.65rem;
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
  font-size: 0.72rem;
  font-weight: 600;
  white-space: nowrap;
}

.billing-status {
  font-size: 0.7rem;
  font-weight: 500;
  color: var(--p-green-500);
}

.billing-widget-footer {
  display: flex;
  justify-content: space-between;
  align-items: center;
  font-size: 0.66rem;
}

.billing-upgrade {
  color: var(--p-primary-500);
  font-weight: 600;
  cursor: pointer;
}

.billing-upgrade:hover {
  text-decoration: underline;
}

:global(.app-dark) .nav-item-accent-green {
  background: linear-gradient(
    135deg,
    color-mix(in srgb, var(--p-green-500) 68%, #052e16),
    color-mix(in srgb, var(--p-emerald-500) 62%, #022c22)
  );
  border: 1px solid color-mix(in srgb, var(--p-green-400) 54%, transparent);
  color: #d1fae5;
  box-shadow: 0 12px 28px rgba(5, 150, 105, 0.28);
}

:global(.app-dark) .nav-item-accent-green:hover {
  background: linear-gradient(
    135deg,
    color-mix(in srgb, var(--p-green-500) 78%, #052e16),
    color-mix(in srgb, var(--p-emerald-500) 72%, #022c22)
  );
  border-color: color-mix(in srgb, var(--p-green-300) 62%, transparent);
  color: #ecfdf5;
}

:global(.app-dark) .nav-icon-wrap-green {
  background: rgba(255, 255, 255, 0.14);
  color: #ecfdf5;
}

.sidebar-content::-webkit-scrollbar {
  width: 10px;
}

.sidebar-content::-webkit-scrollbar-track {
  background: transparent;
}

.sidebar-content::-webkit-scrollbar-thumb {
  background: color-mix(in srgb, var(--p-surface-400) 55%, transparent);
  border-radius: 999px;
  border: 2px solid transparent;
  background-clip: padding-box;
}

.sidebar-content::-webkit-scrollbar-thumb:hover {
  background: color-mix(in srgb, var(--p-surface-500) 65%, transparent);
  background-clip: padding-box;
}

:global(.app-dark) .sidebar-content::-webkit-scrollbar-thumb {
  background: color-mix(in srgb, var(--p-surface-500) 55%, transparent);
  background-clip: padding-box;
}

@media (max-width: 1023px) {
  .app-sidebar {
    width: 272px !important;
    transform: translateX(-100%);
    transition: transform 0.18s ease;
    box-shadow: 0 20px 40px rgba(0, 0, 0, 0.18);
  }

  .app-sidebar.mobile-open {
    transform: translateX(0);
  }
}

</style>
