import type { RouteRecordRaw } from 'vue-router'

export const runnersRoutes: RouteRecordRaw[] = [
  {
    path: 'runners',
    name: 'Runners',
    component: () => import('./views/RunnersView.vue'),
    meta: { title: 'Runners', icon: 'mdi-desktop-classic' },
  },
]
