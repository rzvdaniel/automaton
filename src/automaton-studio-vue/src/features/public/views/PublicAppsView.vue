<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import Button from 'primevue/button'
import ProgressSpinner from 'primevue/progressspinner'
import PublicUserHeader from '../components/PublicUserHeader.vue'
import PublicAppCard from '../components/PublicAppCard.vue'
import { publicAppsService } from '../services/publicApps.service'
import { useAuthStore } from '@/features/auth/stores/auth.store'
import type { PublicUserProfile, PublicAppCard as AppCardType } from '../types/publicApps.types'
import { useToast } from 'primevue/usetoast'

const route = useRoute()
const router = useRouter()
const authStore = useAuthStore()
const toast = useToast()

const loading = ref(true)
const error = ref<string | null>(null)
const profile = ref<PublicUserProfile | null>(null)
const apps = ref<AppCardType[]>([])
const publicUsername = computed(() => (route.params.publicUsername as string) || '')

async function loadUserApps() {
  if (!publicUsername.value) {
    error.value = 'Invalid public username'
    loading.value = false
    return
  }

  loading.value = true
  error.value = null

  try {
    const response = await publicAppsService.getUserApps(publicUsername.value)
    profile.value = response.profile
    apps.value = response.apps
  } catch (err: unknown) {
    if ((err as { response?: { status?: number } })?.response?.status === 404) {
      error.value = 'User not found'
    } else {
      error.value = 'Failed to load apps. Please try again later.'
    }
  } finally {
    loading.value = false
  }
}

function openDetails(app: AppCardType) {
  router.push({
    name: 'public-app-detail',
    params: {
      publicUsername: publicUsername.value,
      appId: app.id,
    },
  })
}

async function toggleStar(app: AppCardType) {
  if (!authStore.isAuthenticated) {
    toast.add({
      severity: 'info',
      summary: 'Sign in required',
      detail: 'Sign in to star apps you like.',
      life: 3000,
    })
    return
  }

  const nextStarred = !app.isStarredByCurrentUser
  const nextCount = Math.max(0, app.starCount + (nextStarred ? 1 : -1))

  app.isStarredByCurrentUser = nextStarred
  app.starCount = nextCount

  try {
    if (nextStarred) {
      await publicAppsService.starApp(app.id)
    } else {
      await publicAppsService.unstarApp(app.id)
    }
  } catch {
    app.isStarredByCurrentUser = !nextStarred
    app.starCount = Math.max(0, app.starCount + (nextStarred ? -1 : 1))

    toast.add({
      severity: 'error',
      summary: 'Star failed',
      detail: 'Could not update your star right now.',
      life: 3000,
    })
  }
}

onMounted(() => {
  void loadUserApps()
})
</script>

<template>
  <div class="public-apps-view">
    <header v-if="!authStore.isAuthenticated" class="public-header">
      <div class="header-inner">
        <router-link to="/" class="logo">
          <span class="logo-text">Automaton</span>
        </router-link>
        <div class="header-actions">
          <router-link to="/login">
            <Button label="Sign In" text size="small" />
          </router-link>
          <router-link to="/register">
            <Button label="Get Started" size="small" />
          </router-link>
        </div>
      </div>
    </header>

    <main class="public-main">
      <div v-if="loading" class="loading-state">
        <ProgressSpinner />
        <p>Loading...</p>
      </div>

      <div v-else-if="error" class="error-state">
        <i class="pi pi-exclamation-triangle"></i>
        <h2>{{ error }}</h2>
        <p v-if="error === 'User not found'">
          This user doesn't exist or hasn't published any apps yet.
        </p>
        <router-link to="/">
          <Button label="Go to Homepage" />
        </router-link>
      </div>

      <template v-else-if="profile">
        <PublicUserHeader :profile="profile" />

        <div class="apps-section">
          <div class="section-header">
            <h2>Published Apps</h2>
          </div>

          <div v-if="apps.length === 0" class="empty-state">
            <i class="pi pi-inbox"></i>
            <h3>No public apps yet</h3>
            <p>This user hasn't published any apps yet. Check back later!</p>
          </div>

          <div v-else class="apps-grid">
            <PublicAppCard
              v-for="app in apps"
              :key="app.id"
              :app="app"
              @details="openDetails"
              @toggle-star="toggleStar"
            />
          </div>
        </div>
      </template>
    </main>

    <footer class="public-footer">
      <div class="footer-inner">
        <p>
          Built with <strong>Automaton</strong> -
          <router-link to="/register">Create your own apps</router-link>
        </p>
      </div>
    </footer>
  </div>
</template>

<style scoped>
.public-apps-view {
  min-height: 100vh;
  display: flex;
  flex-direction: column;
  background: var(--p-surface-ground);
}

.public-header {
  border-bottom: 1px solid var(--p-surface-200);
  background: var(--p-surface-card);
  position: sticky;
  top: 0;
  z-index: 100;
}

:global(.app-dark) .public-header {
  border-color: var(--p-surface-700);
}

.header-inner {
  max-width: 1200px;
  margin: 0 auto;
  padding: 0.75rem 1.5rem;
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.logo {
  text-decoration: none;
}

.logo-text {
  font-size: 1.5rem;
  font-weight: 700;
  color: var(--p-primary-500);
}

.header-actions {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.public-main {
  flex: 1;
  padding-bottom: 2rem;
}

.loading-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 4rem 2rem;
  color: var(--p-text-muted-color);
}

.loading-state p {
  margin-top: 1rem;
}

.error-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 4rem 2rem;
  text-align: center;
}

.error-state i {
  font-size: 4rem;
  color: var(--p-orange-500);
  margin-bottom: 1rem;
}

.error-state h2 {
  margin: 0 0 0.5rem 0;
  color: var(--p-text-color);
}

.error-state p {
  margin: 0 0 1.5rem 0;
  color: var(--p-text-muted-color);
}

.apps-section {
  max-width: 1400px;
  margin: 0 auto;
  padding: 0 1.5rem;
}

.section-header {
  margin-bottom: 1.5rem;
}

.section-header h2 {
  margin: 0;
  font-size: 1.5rem;
  font-weight: 600;
  color: var(--p-text-color);
}

.empty-state {
  text-align: center;
  padding: 4rem 2rem;
  color: var(--p-text-muted-color);
}

.empty-state i {
  font-size: 4rem;
  margin-bottom: 1rem;
  opacity: 0.5;
}

.empty-state h3 {
  margin: 0 0 0.5rem 0;
  color: var(--p-text-color);
}

.empty-state p {
  margin: 0;
}

.apps-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
  gap: 1.5rem 1rem;
  padding-bottom: 1rem;
}

.public-footer {
  border-top: 1px solid var(--p-surface-200);
  background: var(--p-surface-card);
  padding: 1.5rem;
  margin-top: auto;
}

:global(.app-dark) .public-footer {
  border-color: var(--p-surface-700);
}

.footer-inner {
  max-width: 1200px;
  margin: 0 auto;
  text-align: center;
}

.footer-inner p {
  margin: 0;
  color: var(--p-text-muted-color);
  font-size: 0.9rem;
}

.footer-inner a {
  color: var(--p-primary-500);
  text-decoration: none;
}

.footer-inner a:hover {
  text-decoration: underline;
}

@media (max-width: 768px) {
  .apps-grid {
    grid-template-columns: repeat(auto-fill, minmax(200px, 1fr));
  }
}

@media (max-width: 600px) {
  .apps-grid {
    grid-template-columns: repeat(auto-fill, minmax(160px, 1fr));
    gap: 1rem 0.75rem;
  }
}
</style>
