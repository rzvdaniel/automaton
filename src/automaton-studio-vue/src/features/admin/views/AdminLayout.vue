<script setup lang="ts">
import { computed } from 'vue'
import { useAuthStore } from '@/features/auth/stores/auth.store'
import { useRoute, useRouter } from 'vue-router'
import Card from 'primevue/card'
import Button from 'primevue/button'

const authStore = useAuthStore()
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
    label: 'Installation',
    icon: 'pi pi-check-circle',
    to: '/admin/installation',
    routePrefix: '/admin/installation',
  },
  {  
    label: 'Dashboard',
    icon: 'pi pi-chart-bar',
    to: '/admin/dashboard',
    routePrefix: '/admin/dashboard',
  },
  {
    label: 'System',
    icon: 'pi pi-shield',
    to: '/admin/system',
    routePrefix: '/admin/system',
  },
  {
    label: 'Users',
    icon: 'pi pi-users',
    to: '/admin/users',
    routePrefix: '/admin/users',
  },
  {
    label: 'AI Providers',
    icon: 'pi pi-sparkles',
    to: '/admin/ai-providers',
    routePrefix: '/admin/ai-providers',
  },
  {
    label: 'AI Guidance',
    icon: 'pi pi-file-edit',
    to: '/admin/ai-guidance',
    routePrefix: '/admin/ai-guidance',
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
    label: 'Audit Events',
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
]

const visibleNavItems = computed(() => navItems.filter(item => item.to !== '/admin/installation' || authStore.user?.roles?.includes('SysAdmin')))

const isActive = (item: AdminNavItem) => {
  return route.path.startsWith(item.routePrefix)
}

const navigate = (to: string) => {
  router.push(to)
}

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
              v-for="item in visibleNavItems"
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
  height: 100%;
  min-height: 0;
  overflow: hidden;
}

.admin-sidebar {
  width: 220px;
  flex-shrink: 0;
  min-height: 0;
}

.admin-sidebar :deep(.p-card),
.admin-sidebar :deep(.p-card-body),
.admin-sidebar :deep(.p-card-content) {
  height: 100%;
  min-height: 0;
}

.admin-sidebar :deep(.p-card-body) {
  padding: 0.75rem;
}

.admin-sidebar :deep(.p-card-content) {
  padding: 0;
}

.admin-content {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-width: 0;
  min-height: 0;
  overflow: hidden;
}

@media (max-width: 768px) {
  .admin-layout {
    flex-direction: column;
    height: auto;
    min-height: auto;
    overflow: visible;
  }

  .admin-sidebar {
    width: 100%;
  }

  .admin-content {
    overflow: visible;
  }
}
</style>
