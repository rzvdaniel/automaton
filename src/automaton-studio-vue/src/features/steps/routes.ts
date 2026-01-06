import type { RouteRecordRaw } from 'vue-router'

export const stepsRoutes: RouteRecordRaw[] = [
  {
    path: 'steps',
    name: 'Steps',
    component: () => import('./views/StepListView.vue'),
    meta: { title: 'Custom Steps', icon: 'mdi-puzzle' },
  },
  {
    path: 'steps/:id',
    name: 'StepEditor',
    component: () => import('./views/StepEditorView.vue'),
    meta: { title: 'Edit Step', icon: 'mdi-puzzle' },
  },
]
