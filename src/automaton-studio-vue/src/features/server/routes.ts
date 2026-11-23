import type { RouteRecordRaw } from 'vue-router'

// One page, two tabs: /server (Automaton) and /server/runners (Runners).
export const serverRoutes: RouteRecordRaw[] = [
  {
    path: '/server/:tab(runners)?',
    name: 'server',
    component: () => import('./views/ServerView.vue'),
    meta: { title: 'Server' },
  },
]
