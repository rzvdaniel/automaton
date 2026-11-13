<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import Menu from 'primevue/menu'
import type { MenuItem } from 'primevue/menuitem'
import { useOrganizationRole } from '@/features/organizations/composables/useOrganizationRole'
import { useAuthStore } from '@/features/auth/stores/auth.store'
import { adminAppsService } from '@/features/admin/services/adminAppsService'
import { getPublicStoreUrl } from '@/shared/utils/public-store-url'
import { DOCS_LINK, railItems, roleLabel, type RailItem } from './rail-items'

const router = useRouter()
const route = useRoute()
const authStore = useAuthStore()
const { isAppUserOnly, loadOrganizations, loading } = useOrganizationRole()

// Reload on navigation so creating or joining an organization shows the right items at once. The rail keeps the
// last known roles while a reload runs, so items do not flicker on every page change.
const ready = ref(false)
const appUserOnly = ref(false)
watch(loading, (isLoading) => {
  if (!isLoading) appUserOnly.value = isAppUserOnly.value
})
onMounted(async () => {
  await loadOrganizations()
  ready.value = true
})
watch(
  () => route.path,
  () => {
    if (!loading.value) void loadOrganizations()
  },
)

const roles = computed(() => authStore.user?.roles ?? [])
const visible = computed(() => railItems({ roles: roles.value, appUserOnly: appUserOnly.value }))
const userName = computed(() => authStore.userName || authStore.userEmail || 'User')
const userRole = computed(() => roleLabel(roles.value, appUserOnly.value))

// Permission requests waiting for approval, only fetched for system administrators.
const waiting = ref(0)
async function refreshWaiting() {
  waiting.value = roles.value.includes('SysAdmin') ? await adminAppsService.waitingCount() : 0
}
onMounted(refreshWaiting)
watch(() => route.path, refreshWaiting)

const isActive = (item: RailItem) => item.match(route.path)

function go(item: RailItem) {
  void router.push(item.to)
}

const userMenu = ref<InstanceType<typeof Menu> | null>(null)
const userMenuOpen = ref(false)
const userMenuItems = computed<MenuItem[]>(() => [
  {
    label: 'Public profile',
    icon: 'pi pi-external-link',
    command: () => {
      if (authStore.publicUsername) window.open(getPublicStoreUrl(authStore.publicUsername), '_blank', 'noopener')
      else void router.push('/settings')
    },
  },
  { separator: true },
  {
    label: 'Sign out',
    icon: 'pi pi-sign-out',
    command: async () => {
      await authStore.logout()
      await router.push('/login')
    },
  },
])
</script>

<template>
  <aside class="app-sidebar" aria-label="Studio">
    <div class="rail">
      <div class="rail-brand">
        <span class="rail-mark" aria-hidden="true">&gt;_</span>
        <span class="rail-name">Studio</span>
      </div>

      <nav class="rail-nav" aria-label="Main">
        <template v-if="ready">
          <a
            v-for="item in visible.main"
            :key="item.to"
            :href="item.to"
            class="rail-item"
            :class="{ active: isActive(item) }"
            :aria-current="isActive(item) ? 'page' : undefined"
            @click.prevent="go(item)"
          >
            <i :class="item.icon" aria-hidden="true"></i>
            <span>{{ item.label }}</span>
            <span v-if="item.waitingBadge && waiting > 0" class="rail-badge" :aria-label="`${waiting} waiting for approval`">{{ waiting }}</span>
          </a>
        </template>
      </nav>

      <a :href="DOCS_LINK.href" class="rail-docs" target="_blank" rel="noopener">
        <i :class="DOCS_LINK.icon" aria-hidden="true"></i>
        <span>{{ DOCS_LINK.label }}</span>
      </a>

      <button
        type="button"
        class="rail-user"
        aria-haspopup="menu"
        :aria-expanded="userMenuOpen"
        @click="(event) => userMenu?.toggle(event)"
      >
        <span class="rail-user-text">
          <span class="rail-user-name">{{ userName }}</span>
          <span class="rail-user-role">{{ userRole }}</span>
        </span>
        <i class="pi pi-ellipsis-v" aria-hidden="true"></i>
      </button>
      <Menu ref="userMenu" :model="userMenuItems" popup @show="userMenuOpen = true" @hide="userMenuOpen = false" />
    </div>
  </aside>
</template>

<style scoped>
.app-sidebar {
  position: fixed;
  top: 0;
  left: 0;
  height: 100vh;
  z-index: 1000;
  font-family: var(--body);
}

.rail {
  display: flex;
  flex-direction: column;
  height: 100%;
  padding: 1rem 0.75rem;
  gap: 0.25rem;
  overflow-y: auto;
}

.rail-brand {
  display: flex;
  align-items: center;
  gap: 0.6rem;
  padding: 0.25rem 0.5rem 1rem;
}

.rail-mark {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 2rem;
  height: 2rem;
  border-radius: 8px;
  background: var(--signal);
  color: var(--on-signal);
  font: 500 0.95rem var(--mono);
}

.rail-name {
  font: 700 1.15rem var(--display);
  letter-spacing: -0.01em;
  color: var(--rail-ink);
}

.rail-nav {
  display: flex;
  flex-direction: column;
  gap: 0.15rem;
  flex: 1;
}

.rail-item {
  display: flex;
  align-items: center;
  gap: 0.7rem;
  padding: 0.55rem 0.7rem;
  border-radius: 10px;
  color: var(--rail-ink-soft);
  font-weight: 500;
  text-decoration: none;
  cursor: pointer;
}

.rail-item:hover {
  color: var(--rail-ink);
}

.rail-item.active {
  color: var(--rail-active-ink);
  background: var(--rail-active);
}

.rail-item:focus-visible,
.rail-user:focus-visible {
  outline: 2px solid var(--signal);
  outline-offset: 2px;
}

.rail-badge {
  margin-left: auto;
  min-width: 1.3rem;
  padding: 0 0.4rem;
  border-radius: 999px;
  background: var(--signal);
  color: var(--on-signal);
  font: 600 0.75rem var(--mono);
  text-align: center;
}

.rail-item i {
  font-size: 1rem;
  width: 1.1rem;
  text-align: center;
}

.rail-docs {
  display: flex;
  align-items: center;
  gap: 0.6rem;
  padding: 0.4rem 0.7rem;
  font-size: 0.8rem;
  color: var(--rail-ink-soft);
  text-decoration: none;
}

.rail-docs:hover {
  color: var(--rail-ink);
}

.rail-user {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.5rem;
  margin-top: 0.75rem;
  padding: 0.75rem 0.7rem;
  border: 0;
  border-top: 1px solid var(--rail-line);
  background: transparent;
  color: var(--rail-ink-soft);
  font: inherit;
  text-align: left;
  cursor: pointer;
}

.rail-user-text {
  display: flex;
  flex-direction: column;
  min-width: 0;
}

.rail-user-name {
  color: var(--rail-ink);
  font-weight: 600;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.rail-user-role {
  font: 400 0.75rem var(--mono);
  color: var(--rail-ink-soft);
}

/* Phone and tablet: the rail becomes a header with one scrolling row of items */
@media (max-width: 1023px) {
  .app-sidebar {
    height: auto;
  }

  .rail {
    flex-direction: row;
    flex-wrap: wrap;
    align-items: center;
    padding: 0.5rem 0.75rem;
    overflow: visible;
  }

  .rail-brand {
    padding: 0;
  }

  .rail-user {
    margin: 0 0 0 auto;
    padding: 0.35rem 0.5rem;
    border-top: 0;
    max-width: 50%;
  }

  .rail-nav {
    order: 3;
    flex: 1 0 100%;
    flex-direction: row;
    align-items: center;
    gap: 0.25rem;
    overflow-x: auto;
    padding-bottom: 0.25rem;
    scrollbar-width: none;
  }

  .rail-item {
    flex: none;
    padding: 0.45rem 0.75rem;
    white-space: nowrap;
  }

  .rail-docs {
    display: none;
  }
}
</style>
