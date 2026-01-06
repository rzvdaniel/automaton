import type { RouteRecordRaw } from 'vue-router'

export const projectsRoutes: RouteRecordRaw[] = [
  {
    path: '/projects',
    name: 'projects',
    component: () => import('./views/ProjectsListView.vue')
  },
  {
    path: '/projects/:id/settings',
    name: 'project-settings',
    component: () => import('./views/ProjectSettingsView.vue')
  }
]
