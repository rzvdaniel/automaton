import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '@/features/auth/stores/auth.store'

// Import feature routes
import { authRoutes } from '@/features/auth/routes'
import { dashboardRoutes } from '@/features/dashboard/routes'
import { flowsRoutes } from '@/features/flows/routes'
import { appsRoutes } from '@/features/apps/routes'
import { stepsRoutes } from '@/features/steps/routes'
import { runnersRoutes } from '@/features/runners/routes'
import { projectsRoutes } from '@/features/projects/routes'
import { settingsRoutes } from '@/features/settings/routes'
import { downloadsRoutes } from '@/features/downloads/routes'
import { installRoutes } from '@/features/install/routes'
import { publicRoutes } from '@/features/public/routes'

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    // Dashboard (home)
    ...dashboardRoutes,

    // Auth
    ...authRoutes,

    // Install (no auth required)
    ...installRoutes,

    // Features
    ...flowsRoutes,
    ...appsRoutes,
    ...stepsRoutes,
    ...runnersRoutes,
    ...projectsRoutes,
    ...settingsRoutes,
    ...downloadsRoutes,

    // Public pages (no auth required, self-contained layout)
    ...publicRoutes,

    // Catch-all redirect to dashboard
    {
      path: '/:pathMatch(.*)*',
      redirect: '/',
    },
  ],
})

// Navigation guard for authentication
router.beforeEach(async (to, _from, next) => {
  const authStore = useAuthStore()

  // Initialize auth state if not already done
  if (!authStore.isInitialized) {
    await authStore.initialize()
  }

  // Check if route requires authentication
  const requiresAuth = to.meta.requiresAuth !== false && to.meta.layout !== 'auth'

  // If route requires auth and user is not authenticated
  if (requiresAuth && !authStore.isAuthenticated) {
    // Redirect to login with return URL
    next({
      path: '/login',
      query: { redirect: to.fullPath },
    })
    return
  }

  // If user is authenticated and tries to access auth pages (login/register)
  if (authStore.isAuthenticated && to.meta.layout === 'auth') {
    // Redirect to dashboard
    next({ path: '/' })
    return
  }

  next()
})

export default router
