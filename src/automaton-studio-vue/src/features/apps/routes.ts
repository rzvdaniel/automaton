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
    path: '/apps/catalog',
    name: 'apps-catalog',
    component: () => import('./views/AppListView.vue'),
    meta: { appsTab: 'catalog' },
  },
  // The Catalog tab used to be called Install apps.
  { path: '/apps/install', redirect: { name: 'apps-catalog' } },
  {
    path: '/apps/install-source',
    name: 'app-install-source',
    component: () => import('./views/InstallAppView.vue'),
  },
  {
    path: '/apps/install/:sourceAppId',
    name: 'app-install',
    component: () => import('./views/InstallAppView.vue'),
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
