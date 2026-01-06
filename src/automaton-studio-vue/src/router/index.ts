import { createRouter, createWebHistory } from 'vue-router'

// Import feature routes
import { authRoutes } from '@/features/auth/routes'
import { dashboardRoutes } from '@/features/dashboard/routes'
import { flowsRoutes } from '@/features/flows/routes'
import { appsRoutes } from '@/features/apps/routes'
import { stepsRoutes } from '@/features/steps/routes'
import { runnersRoutes } from '@/features/runners/routes'
import { projectsRoutes } from '@/features/projects/routes'
import { settingsRoutes } from '@/features/settings/routes'

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    // Auth routes (public)
    ...authRoutes,

    // Main app routes (protected, wrapped in AppShell layout)
    {
      path: '/',
      component: () => import('@/shared/components/layout/AppShell.vue'),
      children: [
        ...dashboardRoutes,
        ...flowsRoutes,
        ...appsRoutes,
        ...stepsRoutes,
        ...runnersRoutes,
        ...projectsRoutes,
        ...settingsRoutes,
      ],
    },

    // 404 Not Found
    {
      path: '/:pathMatch(.*)*',
      name: 'NotFound',
      component: () => import('@/shared/components/ui/NotFound.vue'),
      meta: { title: 'Not Found', public: true },
    },
  ],
})

// Navigation guard for page title
router.beforeEach((to, _from, next) => {
  const title = to.meta.title as string | undefined
  document.title = title ? `${title} - Automaton Studio` : 'Automaton Studio'
  next()
})

export default router
