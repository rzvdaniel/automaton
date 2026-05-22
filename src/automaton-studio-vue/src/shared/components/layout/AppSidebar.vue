<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { watch } from 'vue'
import Button from 'primevue/button'
import { useOrganizationRole } from '@/features/organizations/composables/useOrganizationRole'
import { useAuthStore } from '@/features/auth/stores/auth.store'
import billingService, { type BillingStatusResponse } from '@/features/settings/services/billingService'
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
  { label: 'Downloads', icon: 'pi pi-download', to: '/downloads', hideForAppUser: true },
  { label: 'Billing', icon: 'pi pi-credit-card', to: '/billing' },
  { label: 'API Keys', icon: 'pi pi-key', to: '/apikeys', hideForAppUser: true },
  { label: 'AI Keys', icon: 'pi pi-key', to: '/ai-keys' },
  { label: 'Settings', icon: 'pi pi-cog', to: '/settings' },
  { label: 'Documentation', icon: 'pi pi-book', to: publicDocsUrl, external: true },
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
  await Promise.all([
    loadBillingStatus(),
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
    { key: 'main', items: filterItems(mainNavItems) },
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
          <i class="pi pi-bolt text-2xl" style="color: var(--p-primary-500)"></i>
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
                :class="['nav-item', { active: isActive(item.to) }]"
                :severity="isActive(item.to) ? 'primary' : 'secondary'"
                :text="!isActive(item.to)"
                rounded
                @click="navigate(item)"
              >
                <i :class="item.icon"></i>
                <span v-if="isExpanded" class="nav-label">{{ item.label }}</span>
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
            :class="['nav-item', { active: isActive(item.to) }]"
            :severity="isActive(item.to) ? 'primary' : 'secondary'"
            :text="!isActive(item.to)"
            rounded
            @click="navigate(item)"
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
  padding: 1rem 0.75rem;
}

.sidebar-logo {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0.5rem;
  margin-bottom: 1.5rem;
}

.sidebar-brand {
  display: flex;
  align-items: center;
  gap: 0.75rem;
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
  gap: 0.5rem;
}

.nav-section {
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
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
  border-radius: 0.75rem;
  transition: all 0.15s ease;
  position: relative;
}

.nav-item i {
  font-size: 1.125rem;
  min-width: 1.25rem;
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
  margin: 0.65rem 0;
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

.billing-status {
  font-size: 0.7rem;
  font-weight: 500;
  color: var(--p-green-500);
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
