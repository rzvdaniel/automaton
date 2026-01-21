import type { RouteRecordRaw } from 'vue-router'

export const publicRoutes: RouteRecordRaw[] = [
  {
    path: '/:username/apps',
    name: 'public-apps',
    component: () => import('./views/PublicAppsView.vue'),
    meta: {
      requiresAuth: false,
      layout: 'none', // No standard layout, self-contained page
    },
  },
]
