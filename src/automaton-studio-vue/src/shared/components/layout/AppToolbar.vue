<script setup lang="ts">
import { ref, computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useUserSettings } from '@/shared/composables/useUserSettings'
import { useAuthStore } from '@/features/auth/stores/auth.store'
import ProjectSwitcher from '@/features/projects/components/ProjectSwitcher.vue'
import Toolbar from 'primevue/toolbar'
import Button from 'primevue/button'
import Menu from 'primevue/menu'
import type { MenuItem } from 'primevue/menuitem'

const { isDark, toggleDarkMode } = useUserSettings()
const authStore = useAuthStore()
const route = useRoute()
const router = useRouter()

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
    settings: 'Settings',
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
          router.push(`/${authStore.publicUsername}`)
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
        <div class="flex items-center gap-2">
          <i class="pi pi-bolt text-xl" style="color: var(--p-primary-500)"></i>
          <span class="font-bold text-xl">Automaton Studio</span>
        </div>
        <div class="border-right" style="height: 1.5rem"></div>
        <ProjectSwitcher />
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
</style>
