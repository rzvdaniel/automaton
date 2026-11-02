import type { RouteRecordRaw } from 'vue-router'

export const authRoutes: RouteRecordRaw[] = [
  {
    path: '/cli/authorize',
    name: 'cli-authorize',
    component: () => import('./views/CliAuthorizeView.vue'),
  },
  {
    path: '/auth/github/callback',
    name: 'github-callback',
    component: () => import('./views/GitHubCallbackView.vue'),
    meta: { layout: 'auth', requiresAuth: false },
  },
  {
    // Signed-in users whose administrator set their password land here before anything else.
    path: '/change-password',
    name: 'change-password',
    component: () => import('./views/ChangePasswordView.vue'),
    meta: { layout: 'auth', requiresAuth: true },
  },
  {
    path: '/login',
    name: 'login',
    component: () => import('./views/LoginView.vue'),
    meta: { layout: 'auth' },
  },
  {
    path: '/register',
    name: 'register',
    component: () => import('./views/RegisterView.vue'),
    meta: { layout: 'auth' },
  },
  {
    path: '/forgot-password',
    name: 'forgot-password',
    component: () => import('./views/ForgotPasswordView.vue'),
    meta: { layout: 'auth' },
  },
  {
    path: '/reset-password',
    name: 'reset-password',
    component: () => import('./views/ResetPasswordView.vue'),
    meta: { layout: 'auth' },
  },
]
