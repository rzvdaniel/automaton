// @vitest-environment jsdom
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import PrimeVue from 'primevue/config'
import ConfirmationService from 'primevue/confirmationservice'
import ToastService from 'primevue/toastservice'
import { createMemoryHistory, createRouter } from 'vue-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useAppEditorStore } from '../stores/app-editor.store'
import { AccessScope, type App } from '../types/apps.types'
import AppSettings from './AppSettings.vue'

vi.mock('../services/apps.service', () => ({
  appsService: {
    getTagSettings: vi.fn().mockResolvedValue({ tags: [] }),
    getTagSuggestions: vi.fn().mockResolvedValue([]),
    getMarketplaceCategories: vi.fn().mockResolvedValue(['Productivity']),
    updateSettings: vi.fn().mockResolvedValue(undefined),
    updateTagSettings: vi.fn().mockResolvedValue(undefined),
  },
}))

import { appsService } from '../services/apps.service'

function app(): App {
  return {
    id: 'app-id', name: 'Clock', summary: '', description: '', isPublished: false,
    isForkable: false, version: 1, requiresAuthentication: false,
    created: '2026-09-01T00:00:00Z', updated: '2026-09-01T00:00:00Z',
    scope: AccessScope.Personal, definition: { version: 1 }, viewCount: 0, starCount: 0,
    isSample: false, isFeatured: false, isProfileFeatured: false, screenshots: [],
    marketplaceStatus: 'Draft', reviewStatus: 'Pending', accessStatus: 'Active',
    developmentMode: 'local', sourceAvailable: true, runtimeSource: 'cli-deploy',
    application: {
      build: { image: null, command: null, outputDirectory: null, timeoutSeconds: 900 },
      runtime: { image: null, command: null, port: 8080 },
      health: { path: null, port: null, startupTimeoutSeconds: 60 },
      storage: { dataDirectory: 'data', mounts: [] },
    },
  }
}

describe('AppSettings', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('keeps Studio-owned metadata but removes runtime-contract editing', async () => {
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/apps/:id', component: { template: '<div />' } }],
    })
    await router.push('/apps/app-id')
    await router.isReady()
    useAppEditorStore().currentApp = app()

    const wrapper = mount(AppSettings, {
      global: {
        plugins: [router, PrimeVue, ConfirmationService, ToastService],
        stubs: { AppDeploymentTargets: true, Select: true },
      },
    })
    await flushPromises()

    expect(wrapper.text()).toContain('Basic Info')
    expect(wrapper.text()).toContain('App Settings')
    expect(wrapper.text()).not.toContain('Publish app')
    expect(wrapper.text()).not.toContain('Unpublish')
    expect(wrapper.text()).not.toContain('Public URL')
    expect(wrapper.text()).not.toContain('Runtime contract')
    expect(wrapper.find('[id="app-name"]').exists()).toBe(true)
    expect(wrapper.find('[id="runtime-port"]').exists()).toBe(false)
    expect(wrapper.text()).toContain('Marketplace category')

    const localProjectButton = wrapper.findAll('button').find((button) => button.text().includes('Local project'))
    if (!localProjectButton) throw new Error('Local project navigation button not found')
    await localProjectButton.trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('automaton pull app-id')
    expect(wrapper.text()).toContain('automaton link app-id')
    expect(wrapper.text()).toContain('automaton deploy')
    expect(wrapper.text()).toContain('does not download source')
  })

  it('keeps category editing in Settings and persists clearing the value', async () => {
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/apps/:id', component: { template: '<div />' } }],
    })
    await router.push('/apps/app-id')
    await router.isReady()
    useAppEditorStore().currentApp = { ...app(), category: 'Productivity' }

    const wrapper = mount(AppSettings, {
      global: {
        plugins: [router, PrimeVue, ConfirmationService, ToastService],
        stubs: {
          AppDeploymentTargets: true,
          Select: {
            props: ['modelValue'],
            emits: ['update:modelValue'],
            template: '<button id="app-category" @click="$emit(\'update:modelValue\', null)">Clear category</button>',
          },
        },
      },
    })
    await flushPromises()
    await wrapper.get('#app-category').trigger('click')
    await wrapper.findAll('button').find((button) => button.text().includes('Save Changes'))?.trigger('click')
    await flushPromises()

    expect(appsService.updateSettings).toHaveBeenCalledWith('app-id', expect.objectContaining({ category: null }))
    expect(useAppEditorStore().currentApp?.category).toBeNull()
  })
})
