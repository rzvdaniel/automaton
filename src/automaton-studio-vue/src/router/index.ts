import { createRouter, createWebHistory } from 'vue-router'
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
    // Auth routes (no app shell)
    ...authRoutes,

    // Main app routes (with app shell)
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
        ...settingsRoutes
      ]
    }
  ]
})

export default router
