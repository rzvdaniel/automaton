// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { AccessScope, DeploymentTargetStatus, type App, type AppDeploymentTarget } from '../types/apps.types'
import AppOperationsOverview from './AppOperationsOverview.vue'

const writeText = vi.fn().mockResolvedValue(undefined)
Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true })

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

function liveTarget(overrides: Partial<AppDeploymentTarget> = {}): AppDeploymentTarget {
  return {
    id: 'live-id',
    appId: 'app-id',
    name: 'Live',
    slug: 'live',
    isDefault: true,
    status: DeploymentTargetStatus.Running,
    genericRuntimeDeployed: true,
    lastSuccessfulDeploymentAt: '2026-09-15T10:00:00Z',
    cleanupPending: false,
    privilegedHostAccessApproved: false,
    routePath: '/',
    runner: {
      id: 'runner-id',
      name: 'Runner',
      purpose: 'Application',
      isHostedRunner: true,
      isOnline: true,
    },
    ...overrides,
  }
}

afterEach(() => vi.clearAllMocks())

describe('AppOperationsOverview', () => {
  it('leads with operational state and local CLI ownership', () => {
    const wrapper = mount(AppOperationsOverview, {
      props: { app: app(), targets: [liveTarget()], applicationState: 'Deployed' },
    })

    expect(wrapper.text()).toContain('Application operations')
    expect(wrapper.text()).toContain('Running')
    expect(wrapper.text()).toContain('Deployed')
    expect(wrapper.text()).toContain('Unpublished')
    expect(wrapper.text()).toContain('Your local Automaton project is the source of truth')
    expect(wrapper.text()).toContain('automaton deploy')
    expect(wrapper.text()).not.toContain('Create File')
  })

  it('keeps historical deployment time separate from current deployment state', () => {
    const wrapper = mount(AppOperationsOverview, {
      props: {
        app: app(),
        targets: [liveTarget({ genericRuntimeDeployed: false })],
        applicationState: 'Not deployed',
      },
    })
    const deploymentCard = wrapper
      .findAll('.status-card')
      .find((card) => card.find('.status-label').text() === 'Deployment')

    expect(deploymentCard?.find('strong').text()).toBe('Not deployed')
    expect(deploymentCard?.text()).toContain('Last successful deployment:')
  })

  it('navigates to operational destinations', async () => {
    const wrapper = mount(AppOperationsOverview, {
      props: { app: app(), targets: [liveTarget()], applicationState: 'Deployed' },
    })

    await wrapper.get('button').trigger('click')

    expect(wrapper.emitted('navigate')?.[0]).toEqual(['runtime'])
  })

  it('copies the CLI deployment command', async () => {
    const wrapper = mount(AppOperationsOverview, {
      props: { app: app(), targets: [], applicationState: 'Not deployed' },
    })

    const copyButton = wrapper.findAll('button').find((button) => button.text().includes('Copy command'))
    if (!copyButton) throw new Error('Copy command button not found')
    await copyButton.trigger('click')

    expect(writeText).toHaveBeenCalledWith('automaton deploy')
  })
})
