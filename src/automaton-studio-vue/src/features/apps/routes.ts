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
    redirect: { name: 'apps' },
  },
  {
    path: '/apps/remix/:sourceAppId',
    alias: '/apps/fork/:sourceAppId',
    name: 'app-remix',
    redirect: { name: 'apps' },
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
