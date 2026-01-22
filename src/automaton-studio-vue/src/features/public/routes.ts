import type { RouteRecordRaw } from 'vue-router'

export const publicRoutes: RouteRecordRaw[] = [
  {
    path: '/:publicUsername',
    name: 'public-apps',
    component: () => import('./views/PublicAppsView.vue'),
    meta: {
      requiresAuth: false,
      layout: 'none', // No standard layout, self-contained page
    },
  },
]
