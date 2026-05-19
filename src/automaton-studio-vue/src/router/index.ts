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
import { organizationsRoutes } from '@/features/organizations/routes'
import { settingsRoutes } from '@/features/settings/routes'
import { downloadsRoutes } from '@/features/downloads/routes'
import { installRoutes } from '@/features/install/routes'
import { adminRoutes } from '@/features/admin/routes'

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    {
      path: '/terms',
      name: 'terms',
      component: () => import('@/shared/views/TermsView.vue'),
      meta: { requiresAuth: false, layout: 'none' },
    },
    {
      path: '/privacy',
      name: 'privacy',
      component: () => import('@/shared/views/PrivacyView.vue'),
      meta: { requiresAuth: false, layout: 'none' },
    },

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
    ...organizationsRoutes,
    ...settingsRoutes,
    ...downloadsRoutes,
    ...adminRoutes,

    // Catch-all redirect to dashboard
    {
      path: '/:pathMatch(.*)*',
      redirect: '/',
    },
  ],
})

// Navigation guard for authentication and authorization
router.beforeEach(async (to, _from, next) => {
  const authStore = useAuthStore()
  const requiresAuth = to.meta.requiresAuth !== false && to.meta.layout !== 'auth'

  // Public routes must not force auth initialization because a stale token can
  // turn an otherwise anonymous page load into a login redirect.
  if (requiresAuth && !authStore.isInitialized) {
    await authStore.initialize()
  }

  // Check if route requires authentication
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

  // Check role-based access control
  const requiredRoles = to.meta.requiresRole as string[] | undefined
  if (requiredRoles && authStore.user) {
    const userRoles = authStore.user.roles || []
    const hasRequiredRole = requiredRoles.some(role => userRoles.includes(role))

    if (!hasRequiredRole) {
      // User doesn't have required role - redirect to dashboard
      next({ path: '/' })
      return
    }
  }

  next()
})

export default router
