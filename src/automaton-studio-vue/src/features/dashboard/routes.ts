import type { RouteRecordRaw } from 'vue-router'

export const dashboardRoutes: RouteRecordRaw[] = [
  {
    path: '',
    name: 'Dashboard',
    component: () => import('./views/DashboardView.vue'),
    meta: { title: 'Dashboard', icon: 'mdi-view-dashboard' },
  },
]
