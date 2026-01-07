import type { RouteRecordRaw } from 'vue-router'

export const stepsRoutes: RouteRecordRaw[] = [
  {
    path: '/steps',
    name: 'steps',
    component: () => import('./views/StepListView.vue'),
  },
  {
    path: '/steps/new',
    name: 'step-new',
    component: () => import('./views/StepEditorView.vue'),
  },
  {
    path: '/steps/:id',
    name: 'step-editor',
    component: () => import('./views/StepEditorView.vue'),
  },
]
