import type { RouteRecordRaw } from 'vue-router'

export const projectsRoutes: RouteRecordRaw[] = [
  {
    path: 'projects',
    name: 'Projects',
    component: () => import('./views/ProjectsListView.vue'),
    meta: { title: 'Projects', icon: 'mdi-domain' },
  },
  {
    path: 'projects/:id/settings',
    name: 'ProjectSettings',
    component: () => import('./views/ProjectSettingsView.vue'),
    meta: { title: 'Project Settings', icon: 'mdi-domain' },
  },
]
