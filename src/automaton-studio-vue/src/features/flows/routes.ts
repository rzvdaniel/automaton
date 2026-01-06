import type { RouteRecordRaw } from 'vue-router'

export const flowsRoutes: RouteRecordRaw[] = [
  {
    path: 'flows',
    name: 'Flows',
    component: () => import('./views/FlowListView.vue'),
    meta: { title: 'Flows', icon: 'mdi-flash' },
  },
  {
    path: 'flows/generate',
    name: 'FlowGeneration',
    component: () => import('./views/FlowGenerationView.vue'),
    meta: { title: 'Generate Flow', icon: 'mdi-flash' },
  },
  {
    path: 'flows/:id',
    name: 'FlowEditor',
    component: () => import('./views/FlowEditorView.vue'),
    meta: { title: 'Edit Flow', icon: 'mdi-flash' },
  },
  {
    path: 'flows/:id/history',
    name: 'FlowHistory',
    component: () => import('./views/FlowHistoryView.vue'),
    meta: { title: 'Flow History', icon: 'mdi-flash' },
  },
]
