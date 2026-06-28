import type { RouteRecordRaw } from 'vue-router'

export const appsRoutes: RouteRecordRaw[] = [
  {
    path: '/apps',
    name: 'apps',
    component: () => import('./views/AppListView.vue'),
  },
  {
    path: '/apps/new',
    name: 'app-new',
    component: () => import('./views/AppEditorView.vue'),
  },
  {
    path: '/apps/remix/:sourceAppId',
    alias: '/apps/fork/:sourceAppId',
    name: 'app-remix',
    component: () => import('./views/AppForkView.vue'),
  },
  {
    path: '/apps/:id',
    name: 'app-editor',
    component: () => import('./views/AppEditorView.vue'),
  },
  {
    path: '/apps/:id/application',
    name: 'app-runtime',
    component: () => import('./views/AppRuntimeView.vue'),
    meta: { layout: 'none' },
  },
]
