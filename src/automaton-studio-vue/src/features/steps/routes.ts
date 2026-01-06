import type { RouteRecordRaw } from 'vue-router'

export const stepsRoutes: RouteRecordRaw[] = [
  {
    path: '/steps',
    name: 'steps',
    component: () => import('./views/StepListView.vue')
  },
  {
    path: '/steps/:id/edit',
    name: 'step-editor',
    component: () => import('./views/StepEditorView.vue')
  }
]
