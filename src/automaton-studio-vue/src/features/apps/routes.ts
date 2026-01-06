import type { RouteRecordRaw } from 'vue-router'

export const appsRoutes: RouteRecordRaw[] = [
  {
    path: '/apps',
    name: 'apps',
    component: () => import('./views/AppListView.vue')
  },
  {
    path: '/apps/:id/edit',
    name: 'app-editor',
    component: () => import('./views/AppEditorView.vue')
  }
]
