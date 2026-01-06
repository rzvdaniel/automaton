import type { RouteRecordRaw } from 'vue-router'

export const appsRoutes: RouteRecordRaw[] = [
  {
    path: 'apps',
    name: 'Apps',
    component: () => import('./views/AppListView.vue'),
    meta: { title: 'Apps', icon: 'mdi-earth' },
  },
  {
    path: 'apps/:id',
    name: 'AppEditor',
    component: () => import('./views/AppEditorView.vue'),
    meta: { title: 'Edit App', icon: 'mdi-earth' },
  },
]
