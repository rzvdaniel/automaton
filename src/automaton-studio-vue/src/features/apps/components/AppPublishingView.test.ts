// @vitest-environment jsdom
import { mount } from '@vue/test-utils'
import PrimeVue from 'primevue/config'
import ToastService from 'primevue/toastservice'
import { describe, expect, it } from 'vitest'
import { AccessScope, type App } from '../types/apps.types'
import AppPublishingView from './AppPublishingView.vue'

function app(overrides: Partial<App> = {}): App {
  return {
    id: 'app-id',
    name: 'Clock',
    summary: '',
    description: '',
    isPublished: false,
    isForkable: false,
    version: 1,
    requiresAuthentication: false,
    created: '2026-09-01T00:00:00Z',
    updated: '2026-09-01T00:00:00Z',
    scope: AccessScope.Personal,
    definition: { version: 1 },
    viewCount: 0,
    starCount: 0,
    isSample: false,
    isFeatured: false,
    isProfileFeatured: false,
    screenshots: [],
    marketplaceStatus: 'Draft',
    reviewStatus: 'Pending',
    accessStatus: 'Active',
    developmentMode: 'local',
    sourceAvailable: true,
    runtimeSource: 'cli-deploy',
    application: {
      build: { image: null, command: null, outputDirectory: null, timeoutSeconds: 900 },
      runtime: { image: null, command: null, port: 8080 },
      health: { path: null, port: null, startupTimeoutSeconds: 60 },
      storage: { dataDirectory: 'data', mounts: [] },
    },
    ...overrides,
  }
}

function mountPublishing(appValue: App, hasLiveTarget = true) {
  return mount(AppPublishingView, {
    props: { app: appValue, hasLiveTarget },
    global: { plugins: [PrimeVue, ToastService] },
  })
}

describe('AppPublishingView', () => {
  it('shows published state, URL, access settings, and shared publishing actions', async () => {
    const wrapper = mountPublishing(app({
      isPublished: true,
      publicUrl: 'clock-app',
      runtimeVisibility: 'public',
      catalogVisibility: 'listed',
      canLaunchPublicly: true,
      isForkable: true,
    }))

    expect(wrapper.text()).toContain('Published')
    expect(wrapper.text()).toContain('clock-app')
    expect(wrapper.text()).toContain('Public runtime')
    expect(wrapper.text()).toContain('Listed in marketplace')
    expect(wrapper.text()).toContain('Enabled')

    const updateButton = wrapper.findAll('button').find((button) => button.text().includes('Update publishing settings'))
    const unpublishButton = wrapper.findAll('button').find((button) => button.text().includes('Unpublish'))
    if (!updateButton || !unpublishButton) throw new Error('Expected publishing actions were not rendered')
    await updateButton.trigger('click')
    await unpublishButton.trigger('click')

    expect(wrapper.emitted('publish')).toHaveLength(1)
    expect(wrapper.emitted('unpublish')).toHaveLength(1)
  })

  it('keeps an unpublished app distinct and requires a Live target before publishing', async () => {
    const wrapper = mountPublishing(app(), false)

    expect(wrapper.text()).toContain('Unpublished')
    expect(wrapper.text()).toContain('Publishing is separate from deploying')
    expect(wrapper.text()).toContain('Create a Live deployment target before publishing.')
    expect(wrapper.find('a').exists()).toBe(false)

    const publishButton = wrapper.findAll('button').find((button) => button.text().includes('Publish Live'))
    if (!publishButton) throw new Error('Publish action was not rendered')
    expect(publishButton.attributes('disabled')).toBeDefined()
    await publishButton.trigger('click')
    expect(wrapper.emitted('publish')).toBeUndefined()
  })
})
