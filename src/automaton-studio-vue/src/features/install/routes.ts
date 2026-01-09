import type { RouteRecordRaw } from 'vue-router'

export const installRoutes: RouteRecordRaw[] = [
  {
    path: '/install',
    name: 'install',
    component: () => import('./views/InstallView.vue'),
    meta: {
      requiresAuth: false,
      layout: 'auth', // Use auth layout (no navigation)
    },
  },
]
