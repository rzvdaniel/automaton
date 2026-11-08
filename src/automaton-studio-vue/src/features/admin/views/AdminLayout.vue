<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useAuthStore } from '@/features/auth/stores/auth.store'
import { useRoute, useRouter } from 'vue-router'
import Card from 'primevue/card'
import Button from 'primevue/button'
import Badge from 'primevue/badge'
import { adminAppsService } from '../services/adminAppsService'

const authStore = useAuthStore()
const route = useRoute()
const router = useRouter()

interface AdminNavItem {
  label: string
  icon: string
  to: string
  routePrefix: string
  badge?: () => number
}

// Permission requests waiting for a system administrator, shown on the Apps entry.
// Only system administrators can approve, so only they see (and pay for) the count.
const isSystemAdministrator = computed(() => authStore.user?.roles?.includes('SysAdmin') ?? false)
const waitingForApproval = ref(0)
async function refreshWaiting() {
  if (!isSystemAdministrator.value) {
    waitingForApproval.value = 0
    return
  }
  try {
    waitingForApproval.value = (await adminAppsService.listApps({ waiting: true, pageSize: 1 })).waitingForApprovalTotal
  } catch {
    waitingForApproval.value = 0
  }
}

const navItems: AdminNavItem[] = [
  {
    label: 'Apps',
    icon: 'pi pi-th-large',
    to: '/admin/apps',
    badge: () => waitingForApproval.value,
    routePrefix: '/admin/apps',
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
]

const visibleNavItems = navItems

const isActive = (item: AdminNavItem) => {
  return route.path.startsWith(item.routePrefix)
}

const navigate = (to: string) => {
  router.push(to)
}

onMounted(() => {
  void refreshWaiting()
})
// Approving or rejecting on an app page changes the count; refresh when moving to another admin page,
// not on query changes such as search keystrokes.
watch(() => route.path, () => {
  void refreshWaiting()
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
              v-for="item in visibleNavItems"
              :key="item.to"
              text
              :class="{ 'surface-hover': isActive(item) }"
              class="w-full justify-content-start"
              :aria-label="item.badge?.() ? `${item.label}, ${item.badge()} waiting for approval` : item.label"
              @click="navigate(item.to)"
            >
              <i :class="[item.icon, 'p-button-icon', 'p-button-icon-left']" aria-hidden="true" />
              <span class="nav-label">{{ item.label }}</span>
              <Badge v-if="item.badge?.()" :value="item.badge()" severity="warn" class="nav-badge" />
            </Button>
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
.nav-label {
  flex: 1;
  text-align: left;
}

.nav-badge {
  margin-left: 0.5rem;
}

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
  /* Pages taller than the window scroll here; full-height workspaces still fit and scroll inside. */
  overflow-x: hidden;
  overflow-y: auto;
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
