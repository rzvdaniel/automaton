import type { RouteRecordRaw } from 'vue-router'

export const publicRoutes: RouteRecordRaw[] = [
  {
    path: '/:publicUsername/apps/:appId',
    name: 'public-app-detail',
    component: () => import('./views/PublicAppDetailView.vue'),
    meta: {
      requiresAuth: false,
      layout: 'none',
    },
  },
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
