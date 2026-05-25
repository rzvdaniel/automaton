<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import Card from 'primevue/card'
import Button from 'primevue/button'

const route = useRoute()
const router = useRouter()

interface AdminNavItem {
  label: string
  icon: string
  to: string
  routePrefix: string
}

const navItems: AdminNavItem[] = [
  {
    label: 'AI Providers',
    icon: 'pi pi-sparkles',
    to: '/admin/ai-providers',
    routePrefix: '/admin/ai-providers',
  },
  {
    label: 'AI Settings',
    icon: 'pi pi-sliders-h',
    to: '/admin/ai-settings',
    routePrefix: '/admin/ai-settings',
  },
  {
    label: 'Marketplace Apps',
    icon: 'pi pi-shop',
    to: '/admin/marketplace-apps',
    routePrefix: '/admin/marketplace-apps',
  },
  {
    label: 'App Activity',
    icon: 'pi pi-chart-line',
    to: '/admin/app-activity',
    routePrefix: '/admin/app-activity',
  },
  {
    label: 'App File Exclusions',
    icon: 'pi pi-filter',
    to: '/admin/app-deploy-rules',
    routePrefix: '/admin/app-deploy-rules',
  },
  {
    label: 'Server Logs',
    icon: 'pi pi-list',
    to: '/admin/logs',
    routePrefix: '/admin/logs',
  },
  {
    label: 'Subscription Plans',
    icon: 'pi pi-credit-card',
    to: '/admin/subscription-plans',
    routePrefix: '/admin/subscription-plans',
  },
  {
    label: 'Hosted Runners',
    icon: 'pi pi-server',
    to: '/admin/hosted-runners',
    routePrefix: '/admin/hosted-runners',
  },
]

const isActive = (item: AdminNavItem) => {
  return route.path.startsWith(item.routePrefix)
}

const navigate = (to: string) => {
  router.push(to)
}

const currentSection = computed(() => {
  const matched = navItems.find((item) => route.path.startsWith(item.routePrefix))
  return matched?.label || 'Administration'
})
</script>

<template>
  <div class="admin-layout">
    <!-- Sidebar -->
    <div class="admin-sidebar">
      <Card class="h-full">
        <template #content>
          <div class="flex flex-column gap-1">
            <div class="text-color-secondary text-sm font-semibold mb-2 px-3">Administration</div>
            <Button
              v-for="item in navItems"
              :key="item.to"
              :label="item.label"
              :icon="item.icon"
              text
              :class="{ 'surface-hover': isActive(item) }"
              class="w-full justify-content-start"
              @click="navigate(item.to)"
            />
          </div>
        </template>
      </Card>
    </div>

    <!-- Content -->
    <div class="admin-content">
      <RouterView />
    </div>
  </div>
</template>

<style scoped>
.admin-layout {
  display: flex;
  gap: 1.5rem;
}

.admin-sidebar {
  width: 220px;
  flex-shrink: 0;
}

.admin-sidebar :deep(.p-card-body) {
  padding: 0.75rem;
}

.admin-sidebar :deep(.p-card-content) {
  padding: 0;
}

.admin-content {
  flex: 1;
  min-width: 0;
}

@media (max-width: 768px) {
  .admin-layout {
    flex-direction: column;
  }

  .admin-sidebar {
    width: 100%;
  }
}
</style>
