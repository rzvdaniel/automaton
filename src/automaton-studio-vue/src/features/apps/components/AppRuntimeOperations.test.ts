// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import PrimeVue from 'primevue/config'
import Select from 'primevue/select'
import { DeploymentTargetStatus, type AppDeploymentTarget } from '../types/apps.types'
import AppRuntimeOperations from './AppRuntimeOperations.vue'

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

function mountRuntime(targets: AppDeploymentTarget[], selectedTarget = targets[0] ?? null) {
  return mount(AppRuntimeOperations, {
    props: {
      app: { id: 'app-id', name: 'Example app' },
      targets,
      selectedTarget,
    },
    global: { plugins: [PrimeVue] },
  })
}

function button(wrapper: ReturnType<typeof mount>, label: string) {
  const found = wrapper.findAll('button').find((candidate) => candidate.text().trim() === label)
  if (!found) throw new Error(`Button not found: ${label}`)
  return found
}

describe('AppRuntimeOperations', () => {
  beforeEach(() => {
    vi.stubGlobal('open', vi.fn())
    vi.stubGlobal('matchMedia', () => ({
      matches: false,
      addListener: vi.fn(),
      removeListener: vi.fn(),
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
    }))
    Object.assign(navigator, { clipboard: { writeText: vi.fn().mockResolvedValue(undefined) } })
  })

  it('shows separate assignment, availability, runtime presence, and target status facts', async () => {
    const wrapper = mountRuntime([target()])

    expect(wrapper.text()).toContain('Automaton-hosted runtime')
    expect(wrapper.text()).toContain('Online')
    expect(wrapper.text()).toContain('Application runtime record')
    expect(wrapper.text()).toContain('Present')
    expect(wrapper.text()).toContain('Running')
    expect(wrapper.text()).not.toContain('Internal runner name')

    await button(wrapper, 'Open app').trigger('click')
    expect(window.open).toHaveBeenCalledWith(expect.stringContaining('/'), '_blank', 'noopener')
  })

  it('shows an offline runtime as unavailable and provides Logs and Deployments navigation', async () => {
    const offline = target({
      status: DeploymentTargetStatus.RunnerOffline,
      runner: { ...target().runner!, isOnline: false },
    })
    const wrapper = mountRuntime([offline])

    expect(wrapper.text()).toContain('Offline')
    expect(wrapper.text()).toContain('The assigned runtime is offline')
    expect(button(wrapper, 'Open app').attributes('disabled')).toBeDefined()

    await wrapper.get('.runtime-actions-card').findAll('button').find((item) => item.text().includes('View logs'))?.trigger('click')
    expect(wrapper.emitted('navigate')?.[0]).toEqual(['logs'])
    await button(wrapper, 'View deployments').trigger('click')
    expect(wrapper.emitted('navigate')?.some(([view]) => view === 'deployments')).toBe(true)
  })

  it('handles an unassigned target without implying deployment or showing a CLI command', () => {
    const unassigned = target({
      status: DeploymentTargetStatus.Unassigned,
      genericRuntimeDeployed: false,
      runner: null,
    })
    const wrapper = mountRuntime([unassigned])

    expect(wrapper.text()).toContain('No runtime assigned')
    expect(wrapper.text()).toContain('Assign a runtime in Deployments')
    expect(wrapper.text()).not.toContain('automaton deploy')
    expect(wrapper.findAll('button').some((item) => item.text().trim() === 'View logs')).toBe(false)
  })

  it('gives CLI guidance for an assigned target without an app runtime, but adds no Studio deploy action', () => {
    const notDeployed = target({
      status: DeploymentTargetStatus.NotDeployed,
      genericRuntimeDeployed: false,
    })
    const wrapper = mountRuntime([notDeployed])

    expect(wrapper.text()).toContain('This environment has no app runtime deployed')
    expect(wrapper.text()).toContain('automaton deploy live')
    expect(wrapper.text()).toContain('Copy deploy command')
    expect(wrapper.findAll('button').some((item) => item.text().trim() === 'Deploy')).toBe(false)
    expect(wrapper.findAll('input, textarea')).toHaveLength(0)
  })

  it('surfaces contradictory Running and missing app-runtime facts as Needs review', () => {
    const contradiction = target({
      status: DeploymentTargetStatus.Running,
      genericRuntimeDeployed: false,
    })
    const wrapper = mountRuntime([contradiction])

    expect(wrapper.text()).toContain('Needs review')
    expect(wrapper.text()).toContain('Running · needs review')
    expect(wrapper.text()).toContain('reports Running')
    expect(wrapper.text()).toContain('no app-runtime record is present')
    expect(button(wrapper, 'Open app').attributes('disabled')).toBeDefined()
    expect(wrapper.text()).not.toContain('Healthy')
    expect(wrapper.text()).not.toContain('automaton deploy')
  })

  it('provides Logs and Deployments destinations from the runtime action surface', async () => {
    const wrapper = mountRuntime([target()])
    const actions = wrapper.get('.runtime-actions-card')

    await actions.findAll('button').find((item) => item.text().includes('View logs'))?.trigger('click')
    expect(wrapper.emitted('navigate')?.[0]).toEqual(['logs'])
    await actions.findAll('button').find((item) => item.text().includes('View deployments'))?.trigger('click')
    expect(wrapper.emitted('navigate')?.[1]).toEqual(['deployments'])
  })

  it('changes the selected environment without exposing runner identifiers or host details', async () => {
    const live = target()
    const selfHosted = target({
      id: 'stage-target-id',
      name: 'Stage',
      slug: 'stage',
      isDefault: false,
      runner: { ...live.runner!, name: 'Secret host name', isHostedRunner: false },
    })
    const wrapper = mountRuntime([live, selfHosted], live)

    const selector = wrapper.getComponent(Select)
    expect(selector.props('options')).toHaveLength(2)
    await selector.vm.$emit('update:modelValue', 'stage')
    expect(wrapper.emitted('selectTarget')?.[0]).toEqual([selfHosted])
    await wrapper.setProps({ selectedTarget: selfHosted })

    expect(wrapper.text()).toContain('Self-hosted runtime')
    expect(wrapper.text()).not.toContain('Automaton-hosted runtime')
    expect(wrapper.text()).not.toContain('Secret host name')
    expect(wrapper.text()).not.toContain('runner-id')
    expect(wrapper.text()).not.toContain('Infrastructure Runner')
    expect(wrapper.text()).not.toContain('Docker')
    expect(wrapper.text()).not.toContain('RabbitMQ')
    expect(wrapper.text()).not.toContain('ConnectionId')
    expect(wrapper.text()).not.toContain('Privileged Host')
  })

  it('guides to Deployments if the app has no target', async () => {
    const wrapper = mountRuntime([])

    expect(wrapper.text()).toContain('No deployment environment is available')
    expect(wrapper.text()).not.toContain('automaton deploy')
    await button(wrapper, 'View deployments').trigger('click')
    expect(wrapper.emitted('navigate')?.[0]).toEqual(['deployments'])
  })

  it('renders a truthful deployment failure and preserves diagnostic navigation', () => {
    const failed = target({ status: DeploymentTargetStatus.Failed, genericRuntimeDeployed: false })
    const wrapper = mountRuntime([failed])

    expect(wrapper.text()).toContain('Deployment failed')
    expect(wrapper.text()).toContain('The latest deployment failed')
    expect(wrapper.text()).toContain('View logs')
    expect(wrapper.text()).toContain('View deployments')
  })
})
