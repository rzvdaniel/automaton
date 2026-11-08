import type { RouteRecordRaw } from 'vue-router'

export const peopleRoutes: RouteRecordRaw[] = [
  {
    path: '/people',
    name: 'people',
    component: () => import('./views/PeopleView.vue'),
    meta: { requiresAuth: true },
  },
]
