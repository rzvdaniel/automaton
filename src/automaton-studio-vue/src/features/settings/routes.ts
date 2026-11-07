import type { RouteRecordRaw } from 'vue-router'

export const settingsRoutes: RouteRecordRaw[] = [
  {
    path: '/settings',
    name: 'settings',
    component: () => import('./views/SettingsView.vue'),
  },
  {
    path: '/apikeys',
    name: 'apikeys',
    component: () => import('./views/ApiKeysView.vue'),
  },
]
