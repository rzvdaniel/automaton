/**
 * Admin feature routes
 * Routes for system administrator management features.
 * Uses AdminLayout as a wrapper with sidebar sub-navigation.
 * To add a new admin section, add a child route here and an entry in AdminLayout's navItems.
 */
import AdminLayout from './views/AdminLayout.vue'
import AdminLogsView from './views/AdminLogsView.vue'
import AdminAppActivityView from './views/AdminAppActivityView.vue'
import AdminAppDeployRulesView from './views/AdminAppDeployRulesView.vue'
import AdminUserDetailView from './views/AdminUserDetailView.vue'
import AdminUserAppsView from './views/AdminUserAppsView.vue'
import AdminAppDetailView from './views/AdminAppDetailView.vue'
import AdminAppsView from './views/AdminAppsView.vue'
import type { RouteRecordRaw } from 'vue-router'

export const adminRoutes: RouteRecordRaw[] = [
  {
    path: '/admin',
    component: AdminLayout,
    meta: {
      requiresRole: ['Admin', 'SysAdmin'],
    },
    children: [
      {
        path: 'installation',
        name: 'admin-installation',
        component: () => import('@/features/install/views/InstallationStatusView.vue'),
        meta: { title: 'Installation', requiresRole: ['SysAdmin'] },
      },
      {
        path: 'backups',
        name: 'admin-backups',
        component: () => import('@/features/install/views/BackupsView.vue'),
        meta: { title: 'Backups', requiresRole: ['SysAdmin'] },
      },
      {
        path: '',
        redirect: '/admin/apps',
      },
      {
        path: 'users',
        name: 'admin-users',
        // The user list now lives on the People page.
        redirect: { name: 'people' },
      },
      {
        path: 'users/:userId',
        name: 'admin-user-detail',
        component: AdminUserDetailView,
        meta: {
          title: 'User Details',
        },
      },
      {
        path: 'users/:userId/apps',
        name: 'admin-user-apps',
        component: AdminUserAppsView,
        meta: {
          title: 'User Apps',
        },
      },
      {
        path: 'apps',
        name: 'admin-apps',
        component: AdminAppsView,
        meta: {
          title: 'Apps',
        },
      },
      {
        path: 'apps/:appId',
        name: 'admin-app-detail',
        component: AdminAppDetailView,
        meta: {
          title: 'App Details',
        },
      },
      {
        path: 'logs',
        name: 'admin-logs',
        component: AdminLogsView,
        meta: {
          title: 'Audit Events',
        },
      },
      {
        path: 'app-deploy-rules',
        name: 'admin-app-deploy-rules',
        component: AdminAppDeployRulesView,
        meta: {
          title: 'App File Exclusions',
        },
      },
      {
        path: 'app-activity',
        name: 'admin-app-activity',
        component: AdminAppActivityView,
        meta: {
          title: 'App Activity',
        },
      },
    ],
  },
]
