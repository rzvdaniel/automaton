<script setup lang="ts">
import { ref, computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useUserSettings } from '@/shared/composables/useUserSettings'
import { useAuthStore } from '@/features/auth/stores/auth.store'
import ProjectSwitcher from '@/features/projects/components/ProjectSwitcher.vue'
import { getPublicStoreUrl } from '@/shared/utils/public-store-url'
import Toolbar from 'primevue/toolbar'
import Button from 'primevue/button'
import Menu from 'primevue/menu'
import type { MenuItem } from 'primevue/menuitem'
import { useSidebarState } from '@/shared/composables/useSidebarState'

const { isDark, toggleDarkMode } = useUserSettings()
const authStore = useAuthStore()
const route = useRoute()
const router = useRouter()
const { isMobile, toggleMobile } = useSidebarState()

// User menu reference
const userMenu = ref<InstanceType<typeof Menu> | null>(null)

// Computed page title from route
const pageTitle = computed(() => {
  const name = route.name as string
  if (!name) return 'Dashboard'
  // Convert route name to title case (e.g., 'flow-list' -> 'Flows')
  const titleMap: Record<string, string> = {
    dashboard: 'Dashboard',
    'flow-list': 'Flows',
    'flow-generation': 'New Flow',
    'app-list': 'Apps',
    'app-editor': 'App Editor',
    'step-list': 'Steps',
    'step-editor': 'Step Editor',
    runners: 'Runners',
    'projects-list': 'Projects',
    'project-settings': 'Project Settings',
    apikeys: 'API Keys',
    'ai-keys': 'AI Keys',
    settings: 'Settings',
    'admin-providers': 'Admin',
    'admin-models': 'Admin',
    'admin-provider-detail': 'Admin',
    'admin-model-detail': 'Admin',
  }
  return titleMap[name] || name.charAt(0).toUpperCase() + name.slice(1)
})

// User display name
const userDisplayName = computed(() => authStore.userName || authStore.userEmail || 'User')

// User menu items
const userMenuItems = computed<MenuItem[]>(() => {
  const items: MenuItem[] = [
    {
      label: userDisplayName.value,
      disabled: true,
      class: 'font-bold',
    },
    {
      separator: true,
    },
    {
      label: 'View Profile',
      icon: 'pi pi-external-link',
      command: () => {
        if (authStore.publicUsername) {
          window.open(getPublicStoreUrl(authStore.publicUsername), '_blank', 'noopener')
        } else {
          router.push('/settings')
        }
      },
    },
    {
      label: 'Settings',
      icon: 'pi pi-cog',
      command: () => router.push('/settings'),
    },
    {
      label: 'Billing',
      icon: 'pi pi-credit-card',
      command: () => router.push('/billing'),
    },
    {
      separator: true,
    },
    {
      label: 'Logout',
      icon: 'pi pi-sign-out',
      command: handleLogout,
    },
  ]

  return items
})

// Toggle user menu
function toggleUserMenu(event: Event) {
  userMenu.value?.toggle(event)
}

// Handle logout
function handleLogout() {
  authStore.logout()
  router.push('/login')
}

// Navigate to settings
function goToSettings() {
  router.push('/settings')
}
</script>

<template>
  <Toolbar class="app-toolbar border-noround">
    <template #start>
      <div class="flex items-center gap-3">
        <Button
          v-if="isMobile"
          icon="pi pi-bars"
          severity="secondary"
          text
          rounded
          aria-label="Open Navigation"
          @click="toggleMobile"
        />
        <div class="flex items-center gap-2">
          <i class="pi pi-bolt text-xl" style="color: var(--p-primary-500)"></i>
          <span class="font-bold text-xl">Automaton Studio</span>
        </div>
        <div class="border-right" style="height: 1.5rem"></div>
        <ProjectSwitcher />
        <span
          v-tooltip.bottom="'Developer Preview\nAPIs and features may change as the platform evolves.'"
          class="preview-badge"
        >
          <span class="preview-badge__full">Developer Preview</span>
          <span class="preview-badge__compact">Preview</span>
        </span>
      </div>
    </template>

    <template #center>
      <span class="text-color-secondary">{{ pageTitle }}</span>
    </template>

    <template #end>
      <div class="flex items-center gap-2">
        <Button
          :icon="isDark ? 'pi pi-sun' : 'pi pi-moon'"
          severity="secondary"
          text
          rounded
          aria-label="Toggle Dark Mode"
          @click="toggleDarkMode"
        />
        <Button
          icon="pi pi-cog"
          severity="secondary"
          text
          rounded
          aria-label="Settings"
          @click="goToSettings"
        />
        <Button
          icon="pi pi-user"
          severity="secondary"
          text
          rounded
          aria-label="User Menu"
          @click="toggleUserMenu"
        />
        <Menu ref="userMenu" :model="userMenuItems" :popup="true" />
      </div>
    </template>
  </Toolbar>
</template>

<style scoped>
/* Dark mode styles handled in main.css */
.app-toolbar {
  padding: 0 1rem;
}

.preview-badge {
  display: inline-flex;
  align-items: center;
  min-height: 1.75rem;
  padding: 0 0.7rem;
  border: 1px solid color-mix(in srgb, var(--p-cyan-500) 34%, transparent);
  border-radius: 999px;
  background: color-mix(in srgb, var(--p-cyan-500) 10%, transparent);
  color: var(--p-cyan-700);
  font-size: 0.72rem;
  font-weight: 700;
  letter-spacing: 0.02em;
  white-space: nowrap;
}

.preview-badge__compact {
  display: none;
}

:global(.app-dark) .preview-badge {
  color: var(--p-cyan-200);
}

@media (max-width: 900px) {
  .preview-badge__full {
    display: none;
  }

  .preview-badge__compact {
    display: inline;
  }
}
</style>
