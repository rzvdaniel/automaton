// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { nextTick } from 'vue'
import PrimeVue from 'primevue/config'
import Select from 'primevue/select'
import type { ApplicationEvent } from '@/shared/types/ai.types'
import { applicationEventsService } from '@/shared/services/api/application-events.service'
import { DeploymentTargetStatus, type AppDeploymentTarget } from '../types/apps.types'
import type { ConsoleEntry } from '../types/console.types'
import AppRuntimeLogs from './AppRuntimeLogs.vue'

vi.mock('@/shared/services/api/application-events.service', () => ({
  applicationEventsService: { getEvents: vi.fn() },
}))

function target(overrides: Partial<AppDeploymentTarget> = {}): AppDeploymentTarget {
  return {
    id: 'live-target-id',
    appId: 'app-id',
    name: 'Live',
    slug: 'live',
    isDefault: true,
    status: DeploymentTargetStatus.Running,
    genericRuntimeDeployed: true,
    cleanupPending: false,
    privilegedHostAccessApproved: false,
    routePath: '/',
    runner: {
      id: 'runner-id',
      name: 'Internal runner name',
      purpose: 'Application',
      isHostedRunner: true,
      isOnline: true,
    },
    ...overrides,
  }
}

function activityEvent(overrides: Partial<ApplicationEvent> = {}): ApplicationEvent {
  return {
    id: 'event-1',
    appId: 'app-id',
    level: 'info',
    category: 'Deployment',
    eventType: 'DeploymentSucceeded',
    message: 'Deployment completed',
    metadataJson: undefined,
    actorUserId: undefined,
    createdAtUtc: '2026-09-17T12:30:00.000Z',
    ...overrides,
  }
}

function page(items: ApplicationEvent[] = [], totalPages = 1) {
  return { items, totalCount: items.length, totalPages, page: 1, pageSize: 50 }
}

function mountLogs(props: Partial<InstanceType<typeof AppRuntimeLogs>['$props']> = {}) {
  return mount(AppRuntimeLogs, {
    props: {
      appId: 'app-id',
      appName: 'Example app',
      targets: [target()],
      selectedTarget: target(),
      liveLogs: [],
      connectionState: 'connected',
      isStreamSubscribed: true,
      streamError: false,
      ...props,
    },
    global: { plugins: [PrimeVue] },
  })
}

describe('AppRuntimeLogs', () => {
  beforeEach(() => {
    vi.mocked(applicationEventsService.getEvents).mockReset()
    vi.mocked(applicationEventsService.getEvents).mockResolvedValue(page())
    vi.stubGlobal('matchMedia', () => ({
      matches: false,
      addListener: vi.fn(),
      removeListener: vi.fn(),
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
    }))
  })

  it('shows a target-aware connected live stream and selects a named environment', async () => {
    const live = target()
    const stage = target({ id: 'stage-id', name: 'Stage', slug: 'stage', isDefault: false })
    const wrapper = mountLogs({ targets: [live, stage] })
    await flushPromises()

    expect(wrapper.text()).toContain('Live')
    expect(wrapper.text()).toContain('Connected')
    expect(wrapper.text()).toContain('Live runtime logs')
    expect(wrapper.text()).toContain('up to 100 recent entries')
    expect(wrapper.text()).not.toContain('Internal runner name')

    const selector = wrapper.getComponent(Select)
    expect(selector.props('options')).toEqual(expect.arrayContaining([
      expect.objectContaining({ label: 'Live · Running', value: 'live' }),
      expect.objectContaining({ label: 'Stage · Running', value: 'stage' }),
    ]))
    await selector.vm.$emit('update:modelValue', 'stage')
    expect(wrapper.emitted('selectTarget')?.[0]).toEqual([stage])
  })

  it('explains when no deployment environment exists and directs to Deployments', async () => {
    const wrapper = mountLogs({ targets: [], selectedTarget: null, isStreamSubscribed: false })
    await flushPromises()

    expect(wrapper.text()).toContain('No environment')
    expect(wrapper.text()).toContain('No deployment environment is available for this app.')
    expect(wrapper.text()).not.toContain('No log output yet.')
    const deployments = wrapper.findAll('button').find((button) => button.text().includes('View deployments'))
    expect(deployments).toBeDefined()
    await deployments?.trigger('click')
    expect(wrapper.emitted('navigate')?.some(([view]) => view === 'deployments')).toBe(true)
  })

  it('distinguishes an unassigned runtime from an empty connected stream', async () => {
    const unassigned = target({
      status: DeploymentTargetStatus.Unassigned,
      genericRuntimeDeployed: false,
      runner: null,
    })
    const wrapper = mountLogs({ selectedTarget: unassigned, isStreamSubscribed: false })
    await flushPromises()

    expect(wrapper.text()).toContain('No runtime assigned')
    expect(wrapper.text()).toContain('No runtime is assigned to this environment.')
    expect(wrapper.text()).not.toContain('No log output yet.')
  })

  it('explains an offline runtime and provides runtime and deployments navigation', async () => {
    const offline = target({
      status: DeploymentTargetStatus.RunnerOffline,
      runner: { ...target().runner!, isOnline: false },
    })
    const wrapper = mountLogs({ selectedTarget: offline })
    await flushPromises()

    expect(wrapper.text()).toContain('Runtime offline')
    expect(wrapper.text()).toContain('The assigned runtime is offline, so live runtime logs are unavailable.')
    await wrapper.findAll('button').find((button) => button.text().includes('View runtime'))?.trigger('click')
    expect(wrapper.emitted('navigate')?.some(([view]) => view === 'runtime')).toBe(true)
    await wrapper.findAll('button').find((button) => button.text().includes('View deployments'))?.trigger('click')
    expect(wrapper.emitted('navigate')?.some(([view]) => view === 'deployments')).toBe(true)
  })

  it('explains when an assigned environment has no deployed app runtime', async () => {
    const notDeployed = target({
      status: DeploymentTargetStatus.NotDeployed,
      genericRuntimeDeployed: false,
    })
    const wrapper = mountLogs({ selectedTarget: notDeployed })
    await flushPromises()

    expect(wrapper.text()).toContain('No app runtime deployed')
    expect(wrapper.text()).toContain('This environment does not currently have a deployed application runtime.')
  })

  it('marks conflicting target/runtime facts as Needs review', async () => {
    const conflict = target({
      status: DeploymentTargetStatus.Running,
      genericRuntimeDeployed: false,
    })
    const wrapper = mountLogs({ selectedTarget: conflict })
    await flushPromises()

    expect(wrapper.text()).toContain('Needs review')
    expect(wrapper.text()).toContain('The target reports Running')
    expect(wrapper.text()).not.toContain('No log output yet.')
  })

  it('shows a deliberate loading state while app activity is loading', async () => {
    let resolveActivity: (result: ReturnType<typeof page>) => void = () => undefined
    const pending = new Promise<ReturnType<typeof page>>((resolve) => {
      resolveActivity = resolve
    })
    vi.mocked(applicationEventsService.getEvents).mockReturnValue(pending)
    const wrapper = mountLogs()
    await nextTick()

    expect(applicationEventsService.getEvents).toHaveBeenCalledTimes(1)
    expect(vi.mocked(applicationEventsService.getEvents).mock.results[0]?.value).toBe(pending)
    expect(wrapper.find('[aria-label="Loading application activity"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('Loading application activity')
    resolveActivity(page())
    await flushPromises()
  })

  it('shows an empty state only when the live stream is connected with no output', async () => {
    const wrapper = mountLogs()
    await flushPromises()

    expect(wrapper.text()).toContain('No log output yet.')
    expect(wrapper.text()).toContain('while this app page is open and the live stream is connected')
  })

  it('shows stream and activity failures without exposing raw exceptions', async () => {
    vi.mocked(applicationEventsService.getEvents).mockRejectedValue(new Error('private server detail'))
    const wrapper = mountLogs({ streamError: true, isStreamSubscribed: false, connectionState: 'disconnected' })
    await flushPromises()

    expect(wrapper.text()).toContain('Connection issue')
    expect(wrapper.text()).toContain('Studio could not connect to live runtime logs')
    expect(wrapper.text()).toContain('Application activity could not be loaded.')
    expect(wrapper.text()).not.toContain('private server detail')
    expect(wrapper.find('[aria-label="Application activity error"]').exists()).toBe(true)
  })

  it('renders multiline runtime output with timestamp, severity, stream, and action context', async () => {
    const output: ConsoleEntry = {
      id: 'runtime-log-1',
      timestamp: new Date('2026-09-17T12:30:45.123Z'),
      type: 'runtime-log',
      stepName: 'HealthController.Check',
      inputs: {},
      result: { success: false, status: 'Error', output: 'first line\nsecond line' },
      source: 'application',
      level: 'error',
      stream: 'stderr',
      deploymentTargetId: 'live-target-id',
    }
    const wrapper = mountLogs({ liveLogs: [output] })
    await flushPromises()

    expect(wrapper.get('.runtime-log-message').text()).toBe('first line\nsecond line')
    expect(wrapper.text()).toContain('Error')
    expect(wrapper.text()).toContain('stderr')
    expect(wrapper.text()).toContain('HealthController.Check')
    expect(wrapper.get('.runtime-log-meta time').text()).toMatch(/\d{2}:30:45\.123/)
    expect(wrapper.text()).not.toContain('Internal runner name')
  })

  it('renders persisted app-wide activity as distinct from runtime output', async () => {
    vi.mocked(applicationEventsService.getEvents).mockResolvedValue(page([
      activityEvent({ message: 'First line\nSecond line' }),
    ]))
    const wrapper = mountLogs()
    await flushPromises()

    expect(wrapper.text()).toContain('Application activity')
    expect(wrapper.text()).toContain('Persisted lifecycle events for this app.')
    expect(wrapper.text()).toContain('not runtime stdout/stderr')
    expect(wrapper.get('.activity-message').text()).toBe('First line\nSecond line')
    await wrapper.get('.activity-summary').trigger('click')
    expect(wrapper.text()).toContain('DeploymentSucceeded')
    expect(wrapper.text()).toContain('Recorded')
  })

  it('keeps pagination and refresh for persisted app activity', async () => {
    vi.mocked(applicationEventsService.getEvents).mockResolvedValue(page([activityEvent()], 2))
    const wrapper = mountLogs()
    await flushPromises()

    expect(wrapper.text()).toContain('Page 1 of 2')
    await wrapper.get('button[aria-label="Next page"]').trigger('click')
    await flushPromises()
    expect(applicationEventsService.getEvents).toHaveBeenLastCalledWith('app-id', expect.objectContaining({ page: 2 }))
    await wrapper.findAll('button').find((button) => button.text().includes('Refresh'))?.trigger('click')
    await flushPromises()
    expect(applicationEventsService.getEvents).toHaveBeenCalledTimes(3)
  })

  it('adds no deployment, source-authoring, or infrastructure-admin controls', async () => {
    const wrapper = mountLogs()
    await flushPromises()

    const buttonLabels = wrapper.findAll('button').map((button) => button.text().trim())
    expect(buttonLabels).not.toContain('Deploy')
    expect(buttonLabels).not.toContain('Restart Hosted Runner')
    expect(buttonLabels).not.toContain('Edit source')
    expect(wrapper.findAll('textarea')).toHaveLength(0)
    expect(wrapper.text()).not.toContain('RabbitMQ')
    expect(wrapper.text()).not.toContain('Docker')
  })
})
