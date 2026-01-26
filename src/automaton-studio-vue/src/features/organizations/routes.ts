import type { RouteRecordRaw } from 'vue-router'

export const organizationsRoutes: RouteRecordRaw[] = [
  {
    path: '/organizations',
    name: 'organizations',
    component: () => import('./views/OrganizationsListView.vue'),
    meta: { requiresAuth: true },
  },
  {
    path: '/organizations/:id',
    name: 'organization-dashboard',
    component: () => import('./views/OrganizationDashboardView.vue'),
    meta: { requiresAuth: true },
  },
  {
    path: '/organizations/:id/members',
    name: 'organization-members',
    component: () => import('./views/OrganizationMembersView.vue'),
    meta: { requiresAuth: true },
  },
  {
    path: '/organizations/:id/apps',
    name: 'organization-apps',
    component: () => import('./views/OrganizationAppsView.vue'),
    meta: { requiresAuth: true },
  },
  {
    path: '/organizations/:id/settings',
    name: 'organization-settings',
    component: () => import('./views/OrganizationSettingsView.vue'),
    meta: { requiresAuth: true },
  },
  {
    path: '/my-apps',
    name: 'assigned-apps',
    component: () => import('./views/AssignedAppsView.vue'),
    meta: { requiresAuth: true },
  },
]
