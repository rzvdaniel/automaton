import type { RouteRecordRaw } from 'vue-router'

export const settingsRoutes: RouteRecordRaw[] = [
  {
    path: '/settings',
    name: 'settings',
    component: () => import('./views/SettingsView.vue'),
  },
  // API keys, approved agents and the CLI guide now live in Settings.
  { path: '/apikeys', name: 'apikeys', redirect: { path: '/settings', hash: '#agents' } },
  { path: '/downloads', name: 'downloads', redirect: { path: '/settings', hash: '#agents' } },
]
