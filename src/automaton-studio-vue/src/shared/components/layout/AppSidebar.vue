<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import Menu from 'primevue/menu'
import type { MenuItem } from 'primevue/menuitem'
import { useOrganizationRole } from '@/features/organizations/composables/useOrganizationRole'
import { useAuthStore } from '@/features/auth/stores/auth.store'
import { getPublicStoreUrl } from '@/shared/utils/public-store-url'
import { railItems, roleLabel, type RailItem } from './rail-items'

const publicDocsUrl = 'https://automaton.monster/docs'

const router = useRouter()
const route = useRoute()
const authStore = useAuthStore()
const { isAppUserOnly, loadOrganizations, loading } = useOrganizationRole()

onMounted(loadOrganizations)

const roles = computed(() => authStore.user?.roles ?? [])
const visible = computed(() => railItems({ roles: roles.value, appUserOnly: isAppUserOnly.value }))
const userName = computed(() => authStore.userName || authStore.userEmail || 'User')
const userRole = computed(() => roleLabel(roles.value, isAppUserOnly.value))

const isActive = (item: RailItem) => !item.external && item.match(route.path)

function go(item: RailItem) {
  if (item.external) window.open(publicDocsUrl, '_blank', 'noopener')
  else void router.push(item.to)
}

const userMenu = ref<InstanceType<typeof Menu> | null>(null)
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
        <template v-if="!loading">
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
          </a>
          <div v-if="visible.more.length" class="rail-group">More</div>
          <a
            v-for="item in visible.more"
            :key="item.to"
            :href="item.to"
            class="rail-item rail-item-quiet"
            :class="{ active: isActive(item) }"
            :aria-current="isActive(item) ? 'page' : undefined"
            @click.prevent="go(item)"
          >
            <i :class="item.icon" aria-hidden="true"></i>
            <span>{{ item.label }}</span>
          </a>
        </template>
      </nav>

      <button type="button" class="rail-user" aria-haspopup="menu" @click="(event) => userMenu?.toggle(event)">
        <span class="rail-user-text">
          <span class="rail-user-name">{{ userName }}</span>
          <span class="rail-user-role">{{ userRole }}</span>
        </span>
        <i class="pi pi-ellipsis-v" aria-hidden="true"></i>
      </button>
      <Menu ref="userMenu" :model="userMenuItems" popup />
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

.rail-item i {
  font-size: 1rem;
  width: 1.1rem;
  text-align: center;
}

.rail-item-quiet {
  font-size: 0.9rem;
}

.rail-group {
  margin: 1rem 0.7rem 0.25rem;
  font: 500 0.7rem var(--mono);
  letter-spacing: 0.08em;
  text-transform: uppercase;
  color: var(--rail-ink-soft);
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

  .rail-group {
    display: none;
  }
}
</style>
