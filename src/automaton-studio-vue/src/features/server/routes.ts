import type { RouteRecordRaw } from 'vue-router'

export const serverRoutes: RouteRecordRaw[] = [
  {
    path: '/server',
    name: 'server',
    component: () => import('./views/ServerView.vue'),
    meta: { title: 'Server', requiresRole: ['Admin', 'SysAdmin'] },
  },
]
