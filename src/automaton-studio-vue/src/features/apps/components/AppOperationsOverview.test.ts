// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { AccessScope, DeploymentTargetStatus, type App, type AppDeploymentTarget } from '../types/apps.types'
import { getAppOperationalSummary } from '../utils/app-operational-summary'
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
    lastDeploymentAt: '2026-09-15T10:00:00Z',
    lastSuccessfulDeploymentAt: '2026-09-15T10:00:00Z',
    cleanupPending: false,
    privilegedHostAccessApproved: false,
    routePath: '/a/app-id/',
    runner: {
      id: 'runner-id',
      name: 'Internal Runner Name',
      purpose: 'Application',
      isHostedRunner: true,
      isOnline: true,
    },
    ...overrides,
  }
}

function mountOverview(
  appOverrides: Partial<App> = {},
  targets: AppDeploymentTarget[] = [liveTarget()],
) {
  const selectedTarget = targets.find((target) => target.isDefault) ?? null
  return mount(AppOperationsOverview, {
    props: {
      app: app(appOverrides),
      targets,
      applicationState: getAppOperationalSummary(selectedTarget),
    },
  })
}

afterEach(() => vi.clearAllMocks())

describe('AppOperationsOverview', () => {
  it('leads with the Live URL and operational facts for a deployed app', () => {
    const wrapper = mountOverview()
    const openApp = wrapper.get('a.open-app-link')

    expect(wrapper.text()).toContain('Deployed')
    expect(wrapper.text()).toContain('Running')
    expect(wrapper.text()).toContain('Live')
    expect(wrapper.text()).toContain('Last successful deployment')
    expect(openApp.text()).toContain('Open app')
    expect(openApp.attributes('href')).toContain('/a/app-id/')
    expect(wrapper.text()).toContain('Unpublished')
    expect(wrapper.text()).toContain('Deployment and publishing are separate.')
    expect(wrapper.text()).not.toContain('Internal Runner Name')
    expect(wrapper.text()).not.toContain('Hosted Runner')
    expect(wrapper.text()).not.toContain('Healthy')
    expect(wrapper.text()).not.toContain('Create File')
    expect(wrapper.find('.local-source-card--primary').exists()).toBe(false)
  })

  it('makes local CLI deployment the primary next step for a never-deployed app', () => {
    const wrapper = mountOverview({}, [liveTarget({
      status: DeploymentTargetStatus.NotDeployed,
      genericRuntimeDeployed: false,
      lastDeploymentAt: null,
      lastSuccessfulDeploymentAt: null,
    })])

    expect(wrapper.text()).toContain('Not deployed')
    expect(wrapper.text()).toContain('No successful deployment yet.')
    expect(wrapper.text()).toContain('The Live app is not deployed yet, so there is no URL to open.')
    expect(wrapper.find('.local-source-card--primary').exists()).toBe(true)
    expect(wrapper.text()).toContain('Deploy from your local project')
    expect(wrapper.text()).toContain('automaton deploy')
    expect(wrapper.find('a.open-app-link').exists()).toBe(false)
  })

  it('shows private Live and published URLs as separate destinations', () => {
    const wrapper = mountOverview({
      isPublished: true,
      publicUrl: 'clock-app',
      runtimeVisibility: 'public',
      canLaunchPublicly: true,
    })
    const liveUrl = wrapper.get('a.open-app-link').attributes('href')
    const publishedUrl = wrapper.get('a.published-link').attributes('href')

    expect(wrapper.text()).toContain('Published')
    expect(wrapper.text()).toContain('Public access enabled')
    expect(wrapper.text()).toContain('Open published app')
    expect(liveUrl).toContain('/a/app-id/')
    expect(publishedUrl).toContain('clock-app')
    expect(liveUrl).not.toBe(publishedUrl)
  })

  it('shows restricted published access without calling it public', () => {
    const wrapper = mountOverview({
      isPublished: true,
      publicUrl: 'clock-app',
      runtimeVisibility: 'private',
      canLaunchPublicly: false,
    })

    expect(wrapper.text()).toContain('Published')
    expect(wrapper.text()).toContain('Private runtime access')
    expect(wrapper.text()).not.toContain('Public access enabled')
    expect(wrapper.get('a.published-link').attributes('href')).toContain('clock-app')
  })

  it('shows an actionable deployment failure with Deployments and Logs destinations', async () => {
    const wrapper = mountOverview({}, [liveTarget({
      status: DeploymentTargetStatus.Failed,
      genericRuntimeDeployed: false,
    })])

    expect(wrapper.text()).toContain('The latest deployment failed')
    expect(wrapper.text()).toContain('Review this target in Deployments')

    await wrapper.get('.diagnostic__primary').trigger('click')
    expect(wrapper.emitted('navigate')?.[0]).toEqual(['deployments'])

    await wrapper.get('.diagnostic__secondary').trigger('click')
    expect(wrapper.emitted('navigate')?.[1]).toEqual(['logs'])
  })

  it('shows a runtime issue with a direct Runtime destination', async () => {
    const wrapper = mountOverview({}, [liveTarget({
      status: DeploymentTargetStatus.RunnerOffline,
      runner: { ...liveTarget().runner!, isOnline: false },
    })])

    expect(wrapper.text()).toContain('Runner offline')
    expect(wrapper.text()).toContain('The assigned runtime is offline')
    await wrapper.get('.diagnostic__primary').trigger('click')
    expect(wrapper.emitted('navigate')?.[0]).toEqual(['runtime'])
  })

  it('routes an unassigned target to Deployments instead of suggesting deploy', async () => {
    const wrapper = mountOverview({}, [liveTarget({
      status: DeploymentTargetStatus.Unassigned,
      runner: null,
      genericRuntimeDeployed: false,
      lastDeploymentAt: null,
      lastSuccessfulDeploymentAt: null,
    })])

    expect(wrapper.text()).toContain('No runtime assigned')
    expect(wrapper.find('.local-source-card').exists()).toBe(false)
    await wrapper.get('.diagnostic__primary').trigger('click')
    expect(wrapper.emitted('navigate')?.[0]).toEqual(['deployments'])
  })

  it('keeps pending runtime cleanup visible without showing implementation details', () => {
    const wrapper = mountOverview({}, [liveTarget({ cleanupPending: true })])

    expect(wrapper.text()).toContain('Runtime cleanup needs attention')
    expect(wrapper.text()).not.toContain('Docker')
    expect(wrapper.text()).not.toContain('container ID')
  })

  it('copies the CLI deployment command from the undeployed guidance', async () => {
    const wrapper = mountOverview({}, [liveTarget({
      status: DeploymentTargetStatus.NotDeployed,
      genericRuntimeDeployed: false,
      lastSuccessfulDeploymentAt: null,
    })])
    const copyButton = wrapper.findAll('button').find((button) => button.text().includes('Copy automaton deploy'))
    if (!copyButton) throw new Error('Copy command button not found')
    await copyButton.trigger('click')

    expect(writeText).toHaveBeenCalledWith('automaton deploy')
  })
})
