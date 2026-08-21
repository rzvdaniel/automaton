// @vitest-environment jsdom
import { describe, expect, it, vi } from 'vitest'
import { shallowMount } from '@vue/test-utils'
import AppListView from './AppListView.vue'

const appsStore = {
  listApps: [],
  filteredApps: [],
  hasApps: false,
  isLoading: false,
  error: null,
  fetchApps: vi.fn().mockResolvedValue(undefined),
  setSearchFilter: vi.fn(),
  setStatusFilter: vi.fn(),
  clearFilters: vi.fn(),
}

vi.mock('../stores/apps.store', () => ({ useAppsStore: () => appsStore }))
vi.mock('@/features/auth/stores/auth.store', () => ({ useAuthStore: () => ({ publicUsername: null }) }))
vi.mock('@/features/projects/stores/projectsStore', () => ({ useProjectsStore: () => ({ activeProjectId: null }) }))
vi.mock('vue-router', () => ({ useRouter: () => ({ push: vi.fn() }) }))
vi.mock('primevue/usetoast', () => ({ useToast: () => ({ add: vi.fn() }) }))
vi.mock('../services/apps.service', () => ({
  appsService: {
    getTagSuggestions: vi.fn().mockResolvedValue([]),
    getTemplates: vi.fn().mockResolvedValue([]),
  },
}))
vi.mock('@/shared/utils/public-store-url', () => ({ getPublicStoreUrl: vi.fn() }))
vi.mock('../utils/public-app-url', () => ({ getPublishedAppUrl: vi.fn() }))

describe('AppListView', () => {
  it('guides users with no apps to the single header create action', () => {
    const wrapper = shallowMount(AppListView, {
      global: {
        stubs: {
          Button: { props: ['label'], template: '<button>{{ label }}</button>' },
          Card: { template: '<section><slot name="content" /></section>' },
          TabMenu: true,
          InputText: true,
          Tag: true,
          Dropdown: true,
          MultiSelect: true,
          Skeleton: true,
          Dialog: true,
          SelectButton: true,
          IconField: true,
          InputIcon: true,
          Message: true,
          AppCard: true,
          PublishDialog: true,
        },
      },
    })

    expect(wrapper.text()).toContain('No apps yet')
    expect(wrapper.text()).toContain('Create and build your first web app with the Create App button above')
    expect(wrapper.findAll('button').filter((button) => button.text() === 'Create App')).toHaveLength(1)
  })
})
