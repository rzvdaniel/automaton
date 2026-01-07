import type { RouteRecordRaw } from 'vue-router'

export const flowsRoutes: RouteRecordRaw[] = [
  {
    path: '/flows',
    name: 'flows',
    component: () => import('./views/FlowListView.vue'),
  },
  {
    path: '/flows/generate',
    name: 'flow-generation',
    component: () => import('./views/FlowGenerationView.vue'),
  },
]
