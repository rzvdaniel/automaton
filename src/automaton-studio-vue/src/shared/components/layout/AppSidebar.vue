<script setup lang="ts">
import { ref, computed } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import Button from 'primevue/button'

const router = useRouter()
const route = useRoute()
const isExpanded = ref(false)

interface NavItem {
  label: string
  icon: string
  to: string
  divider?: boolean
}

const navItems: NavItem[] = [
  { label: 'Dashboard', icon: 'pi pi-home', to: '/' },
  // { label: 'Flows', icon: 'pi pi-bolt', to: '/flows' },
  { label: 'Apps', icon: 'pi pi-globe', to: '/apps' },
  // { label: 'Steps', icon: 'pi pi-code', to: '/steps' },
  { label: 'Runners', icon: 'pi pi-desktop', to: '/runners' },
  { label: 'Projects', icon: 'pi pi-building', to: '/projects' },
  { label: 'Organizations', icon: 'pi pi-users', to: '/organizations' },
]

const bottomNavItems: NavItem[] = [
  { label: 'Downloads', icon: 'pi pi-download', to: '/downloads' },
  { label: 'Settings', icon: 'pi pi-cog', to: '/settings' },
  { label: 'Documentation', icon: 'pi pi-book', to: '/docs' },
]

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
        <div v-for="item in navItems" :key="item.to" class="nav-item-wrapper">
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

      <!-- Bottom Navigation -->
      <nav class="sidebar-nav sidebar-nav-bottom">
        <div class="nav-divider"></div>
        <div v-for="item in bottomNavItems" :key="item.to" class="nav-item-wrapper">
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

.nav-divider {
  height: 1px;
  background-color: var(--p-surface-200);
  margin: 0.5rem 0;
}

.sidebar-nav-bottom {
  margin-top: auto;
}
</style>
