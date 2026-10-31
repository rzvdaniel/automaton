import type { RouteRecordRaw } from 'vue-router'

export const downloadsRoutes: RouteRecordRaw[] = [
  {
    path: '/downloads',
    name: 'downloads',
    component: () => import('./views/DownloadsView.vue'),
    meta: {
      title: 'Automaton CLI',
    },
  },
]
