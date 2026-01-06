import type { RouteRecordRaw } from 'vue-router'

export const settingsRoutes: RouteRecordRaw[] = [
  {
    path: 'settings',
    name: 'Settings',
    component: () => import('./views/SettingsView.vue'),
    meta: { title: 'Settings', icon: 'mdi-cog' },
  },
]
