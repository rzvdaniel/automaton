// @vitest-environment jsdom
import { describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
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
  clearError: vi.fn(),
}

vi.mock('../stores/apps.store', () => ({ useAppsStore: () => appsStore }))
vi.mock('@/features/auth/stores/auth.store', () => ({ useAuthStore: () => ({ publicUsername: null }) }))
vi.mock('@/features/projects/stores/projectsStore', () => ({ useProjectsStore: () => ({ activeProjectId: null }) }))
vi.mock('../services/apps.service', () => ({
  appsService: {
    getTemplates: vi.fn().mockResolvedValue([]),
    getTagSuggestions: vi.fn().mockResolvedValue([]),
  },
}))
vi.mock('vue-router', () => ({ useRouter: () => ({ push: vi.fn() }) }))
vi.mock('primevue/usetoast', () => ({ useToast: () => ({ add: vi.fn() }) }))

const stubs = {
  Card: { template: '<section><slot name="content" /><slot /></section>' },
  Button: { props: ['label'], template: '<button><slot />{{ label }}</button>' },
  InputText: true,
  Tag: true,
  Dropdown: true,
  MultiSelect: true,
  Skeleton: true,
  Dialog: true,
  SelectButton: true,
  IconField: { template: '<div><slot /></div>' },
  InputIcon: true,
  TabMenu: true,
  Message: { template: '<div><slot /></div>' },
  AppCard: true,
  PublishDialog: true,
}

describe('AppListView', () => {
  it('hides only the decorative no-apps icon from assistive technology', () => {
    const wrapper = mount(AppListView, { global: { stubs } })

    const emptyState = wrapper.get('h3').element.parentElement
    expect(emptyState?.querySelector('.pi-globe')?.getAttribute('aria-hidden')).toBe('true')
    expect(emptyState?.textContent).toContain('No apps yet')
    expect(emptyState?.textContent).toContain('Create an app from scratch, import an existing project, or start from a template.')
    expect(wrapper.find('.pi-search').attributes('aria-hidden')).toBeUndefined()
  })
})
