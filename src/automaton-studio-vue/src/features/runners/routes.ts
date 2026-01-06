import type { RouteRecordRaw } from 'vue-router'

export const runnersRoutes: RouteRecordRaw[] = [
  {
    path: '/runners',
    name: 'runners',
    component: () => import('./views/RunnersView.vue')
  }
]
