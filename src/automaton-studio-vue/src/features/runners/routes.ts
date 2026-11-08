import type { RouteRecordRaw } from 'vue-router'

// Runners now live on the Server page.
export const runnersRoutes: RouteRecordRaw[] = [
  { path: '/runners', name: 'runners', redirect: { name: 'server' } },
]
