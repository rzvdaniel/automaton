// @vitest-environment jsdom
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import PrimeVue from 'primevue/config'
import { createMemoryHistory, createRouter } from 'vue-router'
import { describe, expect, it, vi } from 'vitest'
import type { AppListItem } from '../types/apps.types'
import AppCard from './AppCard.vue'

vi.mock('primevue/useconfirm', () => ({ useConfirm: () => ({ require: vi.fn() }) }))
vi.mock('primevue/usetoast', () => ({ useToast: () => ({ add: vi.fn() }) }))

function app(overrides: Partial<AppListItem> = {}): AppListItem {
  return {
    id: 'app-id',
    name: 'Clock',
    summary: '',
    description: '',
    tags: [],
    isPublished: false,
    updatedAtUtc: '2026-09-01T00:00:00Z',
    canDelete: true,
    liveEnvironment: null,
    runtime: { genericRuntimeDeployed: false },
    ...overrides,
  }
}

describe('AppCard', () => {
  it('routes undeployed apps to local setup instead of implying a Studio deploy action', async () => {
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/apps/:id', component: { template: '<div />' } }],
    })
    await router.push('/apps')
    await router.isReady()

    const wrapper = mount(AppCard, {
      props: { app: app() },
      global: {
        plugins: [router, createPinia(), PrimeVue],
        stubs: { Menu: true, AppCoverPlaceholder: true },
      },
    })

    await wrapper.get('button.primary-action').trigger('click')
    await flushPromises()

    expect(router.currentRoute.value.path).toBe('/apps/app-id')
    expect(wrapper.text()).toContain('View local setup')
    expect(wrapper.findAll('button').some((button) => button.text() === 'Deploy')).toBe(false)
  })
})
