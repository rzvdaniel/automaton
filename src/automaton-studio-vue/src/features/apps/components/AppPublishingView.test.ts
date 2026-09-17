// @vitest-environment jsdom
import { mount } from '@vue/test-utils'
import PrimeVue from 'primevue/config'
import ToastService from 'primevue/toastservice'
import { describe, expect, it } from 'vitest'
import { AccessScope, DeploymentTargetStatus, type App, type AppDeploymentTarget } from '../types/apps.types'
import { getAppOperationalSummary } from '../utils/app-operational-summary'
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
    reviewStatus: 'Approved',
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

function target(overrides: Partial<AppDeploymentTarget> = {}): AppDeploymentTarget {
  return {
    id: 'live-id',
    appId: 'app-id',
    name: 'Live',
    slug: 'live',
    isDefault: true,
    status: DeploymentTargetStatus.Running,
    genericRuntimeDeployed: true,
    lastDeploymentAt: '2026-09-15T10:00:00Z',
    lastSuccessfulDeploymentAt: '2026-09-15T10:00:00Z',
    cleanupPending: false,
    privilegedHostAccessApproved: false,
    routePath: '/a/app-id/',
    runner: { id: 'runner-id', name: 'Runner', purpose: 'Application', isHostedRunner: true, isOnline: true },
    ...overrides,
  }
}

function mountPublishing(appValue: App, overrides: { hasLiveTarget?: boolean; target?: AppDeploymentTarget } = {}) {
  return mount(AppPublishingView, {
    props: {
      app: appValue,
      hasLiveTarget: overrides.hasLiveTarget ?? true,
      operationalState: getAppOperationalSummary(
        overrides.target ?? (overrides.hasLiveTarget === false ? null : target()),
      ),
    },
    global: { plugins: [PrimeVue, ToastService] },
  })
}

describe('AppPublishingView', () => {
  it('shows the public URL and centralized publishing actions for a public app', async () => {
    const wrapper = mountPublishing(app({
      isPublished: true,
      publicUrl: 'clock-app',
      runtimeVisibility: 'public',
      catalogVisibility: 'listed',
      marketplacePreviewMode: 'launchable',
      canLaunchPublicly: true,
      isForkable: true,
    }))

    expect(wrapper.text()).toContain('Published')
    expect(wrapper.text()).toContain('clock-app')
    expect(wrapper.text()).toContain('Public runtime')
    expect(wrapper.text()).toContain('Listed in marketplace')
    expect(wrapper.text()).toContain('Discoverable in the marketplace')
    expect(wrapper.text()).not.toContain('Remixing')
    expect(wrapper.text()).not.toContain('remix')
    expect(wrapper.text()).not.toContain('fork')
    expect(wrapper.text()).toContain('Unpublishing removes public access; it does not undeploy the Live runtime.')

    const updateButton = wrapper.findAll('button').find((button) => button.text().includes('Update publishing settings'))
    const unpublishButton = wrapper.findAll('button').find((button) => button.text().includes('Unpublish'))
    if (!updateButton || !unpublishButton) throw new Error('Expected publishing actions were not rendered')
    await updateButton.trigger('click')
    await unpublishButton.trigger('click')
    expect(wrapper.emitted('publish')).toHaveLength(1)
    expect(wrapper.emitted('unpublish')).toHaveLength(1)
  })

  it('allows saving publish settings without a Live target and explains deployment is separate', async () => {
    const wrapper = mountPublishing(app(), { hasLiveTarget: false })

    expect(wrapper.text()).toContain('Unpublished')
    expect(wrapper.text()).toContain('Publishing settings can still be saved')
    expect(wrapper.text()).toContain('Publishing is separate from deploying the Live runtime')
    expect(wrapper.find('a').exists()).toBe(false)

    const publishButton = wrapper.findAll('button').find((button) => button.text().includes('Publish app'))
    if (!publishButton) throw new Error('Publish action was not rendered')
    expect(publishButton.attributes('disabled')).toBeUndefined()
    await publishButton.trigger('click')
    expect(wrapper.emitted('publish')).toHaveLength(1)
  })

  it('does not expose a runtime URL for a listed private media-only app', () => {
    const wrapper = mountPublishing(app({
      isPublished: true,
      publicUrl: 'private-clock',
      runtimeVisibility: 'private',
      catalogVisibility: 'listed',
      marketplacePreviewMode: 'mediaOnly',
    }))

    expect(wrapper.text()).toContain('Published')
    expect(wrapper.text()).toContain('Discoverable with a media-only preview; runtime launch is private')
    expect(wrapper.text()).toContain('no public runtime URL')
    expect(wrapper.find('a[href*="private-clock"]').exists()).toBe(false)
  })

  it('distinguishes saved listing settings from marketplace approval', () => {
    const wrapper = mountPublishing(app({
      isPublished: true,
      publicUrl: 'clock-app',
      runtimeVisibility: 'public',
      catalogVisibility: 'listed',
      marketplacePreviewMode: 'launchable',
      reviewStatus: 'Pending',
    }))

    expect(wrapper.text()).toContain('Published')
    expect(wrapper.text()).toContain('Listed setting saved; marketplace review is pending')
    expect(wrapper.find('a[href*="clock-app"]').exists()).toBe(true)
  })

  it.each([
    ['missing slug', { isPublished: true, runtimeVisibility: 'public' as const }],
    ['public runtime that requires sign-in', { isPublished: true, publicUrl: 'clock-app', runtimeVisibility: 'public' as const, requiresAuthentication: true }],
    ['listed private runtime with launchable preview', { isPublished: true, publicUrl: 'clock-app', runtimeVisibility: 'private' as const, catalogVisibility: 'listed' as const, marketplacePreviewMode: 'launchable' as const }],
    ['unpublished with stale public visibility', { isPublished: false, runtimeVisibility: 'public' as const }],
  ])('marks contradictory state for review (%s)', (_label, overrides) => {
    const wrapper = mountPublishing(app(overrides))
    expect(wrapper.text()).toContain('Needs review')
    expect(wrapper.text()).toContain('Publishing needs review')
  })

  it('offers runtime and deployment destinations when the assigned runtime is offline', async () => {
    const wrapper = mountPublishing(app({ isPublished: true, publicUrl: 'clock-app', runtimeVisibility: 'public' }), {
      target: target({ status: DeploymentTargetStatus.RunnerOffline, runner: { ...target().runner!, isOnline: false } }),
    })

    expect(wrapper.text()).toContain('The Live runtime is runner offline')
    const runtime = wrapper.findAll('button').find((button) => button.text().includes('View runtime'))
    const deployments = wrapper.findAll('button').find((button) => button.text().includes('View deployments'))
    if (!runtime || !deployments) throw new Error('Operational destinations were not rendered')
    await runtime.trigger('click')
    await deployments.trigger('click')
    expect(wrapper.emitted('navigate')?.[0]).toEqual(['runtime'])
    expect(wrapper.emitted('navigate')?.[1]).toEqual(['deployments'])
  })
})
