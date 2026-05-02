import type { RouteRecordRaw } from 'vue-router'

export const docsRoutes: RouteRecordRaw[] = [
  {
    path: '/docs',
    name: 'docs-home',
    component: () => import('./views/DocsCenterView.vue'),
    meta: {
      title: 'Documentation',
    },
  },
  {
    path: '/docs/:slug',
    name: 'docs-section',
    component: () => import('./views/DocsCenterView.vue'),
    meta: {
      title: 'Documentation',
    },
  },
]