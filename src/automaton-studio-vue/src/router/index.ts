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
    // Dashboard (home)
    ...dashboardRoutes,

    // Auth
    ...authRoutes,

    // Features
    ...flowsRoutes,
    ...appsRoutes,
    ...stepsRoutes,
    ...runnersRoutes,
    ...projectsRoutes,
    ...settingsRoutes,

    // Catch-all redirect to dashboard
    {
      path: '/:pathMatch(.*)*',
      redirect: '/',
    },
  ],
})

export default router
