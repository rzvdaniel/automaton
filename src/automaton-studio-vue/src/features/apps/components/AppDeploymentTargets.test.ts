// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import PrimeVue from 'primevue/config'
import ToastService from 'primevue/toastservice'
import { DeploymentTargetStatus, type AppDeploymentTarget } from '../types/apps.types'
import AppDeploymentTargets from './AppDeploymentTargets.vue'

const mocks = vi.hoisted(() => ({
  listTargets: vi.fn(),
  listRunners: vi.fn(),
  confirm: vi.fn(),
  toast: vi.fn(),
}))

vi.mock('../services/apps.service', () => ({
  appsService: { listDeploymentTargets: mocks.listTargets },
}))
vi.mock('@/features/runners/services/runners.service', () => ({
  runnersService: { list: mocks.listRunners },
}))
vi.mock('primevue/useconfirm', () => ({ useConfirm: () => ({ require: mocks.confirm }) }))
vi.mock('primevue/usetoast', () => ({ useToast: () => ({ add: mocks.toast }) }))

function target(overrides: Partial<AppDeploymentTarget> = {}): AppDeploymentTarget {
  return {
    id: 'live-id',
    appId: 'app-id',
    name: 'Live',
    slug: 'live',
    isDefault: true,
    status: DeploymentTargetStatus.Running,
    frontendDeployed: true,
    backendDeployed: true,
    cleanupPending: false,
    lastSuccessfulDeploymentAt: '2026-08-04T18:42:00Z',
    routePath: '/private/app-id',
    runner: {
      id: 'runner-id',
      name: 'hidden-hosted-runner',
      purpose: 'Application',
      isHostedRunner: true,
      isOnline: true,
    },
    ...overrides,
  }
}

async function mountTargets(items: AppDeploymentTarget[]) {
  mocks.listTargets.mockResolvedValue(items)
  mocks.listRunners.mockResolvedValue([])
  const wrapper = mount(AppDeploymentTargets, {
    props: { appId: 'app-id' },
    global: {
      plugins: [createPinia(), PrimeVue, ToastService],
      stubs: { Dialog: true, Select: true, Checkbox: true },
    },
  })
  await flushPromises()
  return wrapper
}

function buttonByText(wrapper: ReturnType<typeof mount>, text: string) {
  const button = wrapper.findAll('button').find((candidate) => candidate.text().includes(text))
  if (!button) throw new Error(`Button not found: ${text}`)
  return button
}

describe('AppDeploymentTargets', () => {
  beforeEach(() => vi.clearAllMocks())

  it('separates the Live deployment from additional targets and hides hosted Runner names', async () => {
    const wrapper = await mountTargets([
      target(),
      target({ id: 'canary-id', name: 'Canary', slug: 'canary', isDefault: false }),
    ])

    expect(wrapper.text()).toContain('Live environment')
    expect(wrapper.text()).toContain('Additional environments')
    expect(wrapper.text()).toContain('Automaton Hosted')
    expect(wrapper.text()).not.toContain('hidden-hosted-runner')
    expect(wrapper.text()).toContain('Canary')
    expect(wrapper.findAll('.p-tag').filter((tag) => tag.text() === 'LIVE')).toHaveLength(1)
  })

  it('confirms promotion without redeployment and does not render Live deletion', async () => {
    const canary = target({ id: 'canary-id', name: 'Canary', slug: 'canary', isDefault: false })
    const wrapper = await mountTargets([target(), canary])

    await buttonByText(wrapper, 'Promote to Live').trigger('click')
    expect(mocks.confirm).toHaveBeenCalledWith(
      expect.objectContaining({
        message: expect.stringContaining('does not redeploy either environment'),
      })
    )
    const menuButton = wrapper.find('[aria-label="More actions for Canary"]')
    await menuButton.trigger('click')
    expect(document.body.textContent).toContain('Delete environment')
    expect(document.body.textContent).toContain('View logs')
    expect(document.body.textContent).toContain('Edit environment')
  })

  it('shows raw identifiers only after opening Advanced details', async () => {
    const wrapper = await mountTargets([target()])
    expect(wrapper.text()).not.toContain('Target ID: live-id')

    const disclosure = buttonByText(wrapper, 'Advanced details')
    expect(disclosure.attributes('aria-expanded')).toBe('false')
    await disclosure.trigger('click')
    expect(disclosure.attributes('aria-expanded')).toBe('true')
    expect(wrapper.text()).toContain('Target ID: live-id')
    expect(wrapper.text()).toContain('Environment slug: live')
    expect(wrapper.text()).toContain('Runtime transport: Direct edge routing')
    expect(wrapper.text()).toContain('Runtime route: /private/app-id')
    expect(wrapper.text()).toContain('Internal preview URL:')
  })

  it('does not offer promotion for an unassigned environment', async () => {
    const unassigned = target({
      id: 'unassigned-id',
      name: 'Default',
      slug: 'default',
      isDefault: false,
      runner: null,
      frontendDeployed: false,
      backendDeployed: false,
      status: DeploymentTargetStatus.Unassigned,
      lastSuccessfulDeploymentAt: null,
    })
    const wrapper = await mountTargets([target(), unassigned])

    const promote = buttonByText(wrapper, 'Promote to Live')
    expect(promote.attributes('disabled')).toBeDefined()
    expect(wrapper.text()).toContain('No runtime assigned')
    expect(wrapper.text()).not.toContain('No destination assigned')
  })

  it('shows privileged access separately from the self-hosted destination', async () => {
    const privileged = target({
      id: 'privileged-id',
      name: 'Privileged',
      slug: 'privileged',
      isDefault: false,
      runner: {
        id: 'privileged-runner',
        name: 'Host',
        purpose: 'Application',
        applicationProfile: 'PrivilegedHost',
        isHostedRunner: false,
        isOnline: true,
      },
    })
    const standard = target({
      id: 'standard-id',
      name: 'Standard',
      slug: 'standard',
      isDefault: false,
      runner: {
        id: 'standard-runner',
        name: 'Host',
        purpose: 'Application',
        applicationProfile: 'Standard',
        isHostedRunner: false,
        isOnline: true,
      },
    })
    const wrapper = await mountTargets([target(), privileged, standard])

    expect(wrapper.text()).toContain('Self-hosted')
    expect(wrapper.text()).toContain('Privileged access')
    expect(wrapper.text()).not.toContain('Privileged Host')
    expect(
      wrapper.findAll('.p-tag').filter((tag) => tag.text() === 'Privileged access')
    ).toHaveLength(1)
  })

  it('only enables promotion when canPromote permits it', async () => {
    const eligible = target({
      id: 'eligible-id',
      name: 'Eligible',
      slug: 'eligible',
      isDefault: false,
    })
    const offline = target({
      id: 'offline-id',
      name: 'Offline',
      slug: 'offline',
      isDefault: false,
      runner: {
        id: 'offline-runner',
        name: 'Offline',
        purpose: 'Application',
        isHostedRunner: false,
        isOnline: false,
      },
      status: DeploymentTargetStatus.RunnerOffline,
    })
    const wrapper = await mountTargets([target(), eligible, offline])

    expect(
      wrapper.findAll('button').filter((button) => button.text().includes('Promote to Live'))
    ).toHaveLength(2)
    const promotionButtons = wrapper
      .findAll('button')
      .filter((button) => button.text().includes('Promote to Live'))
    expect(promotionButtons[0]?.attributes('disabled')).toBeUndefined()
    expect(promotionButtons[1]?.attributes('disabled')).toBeDefined()
  })

  it('keeps deployment events and Delete confirmation behind the overflow menu', async () => {
    const canary = target({ id: 'canary-id', name: 'Canary', slug: 'canary', isDefault: false })
    const wrapper = await mountTargets([target(), canary])

    const deployButton = wrapper
      .findAll('.target-card')[1]
      ?.findAll('button')
      .find((button) => button.text().includes('Deploy'))
    expect(deployButton).toBeDefined()
    await deployButton?.trigger('click')
    expect(wrapper.emitted('deploy')?.[0]).toEqual([canary])

    await wrapper.find('[aria-label="More actions for Canary"]').trigger('click')
    const deleteItem = Array.from(document.querySelectorAll('li')).find((item) =>
      item.textContent?.includes('Delete environment')
    )
    expect(deleteItem).toBeDefined()
    const deleteLink = deleteItem?.querySelector('a') as HTMLElement | null
    deleteLink?.click()
    expect(mocks.confirm).toHaveBeenCalledWith(
      expect.objectContaining({
        header: 'Delete environment?',
        acceptClass: 'p-button-danger',
      })
    )
  })

  it('keeps routes target-specific regardless of the destination type', async () => {
    const hostedCanary = target({
      id: 'canary-id',
      name: 'Canary',
      slug: 'canary',
      isDefault: false,
      routePath: '/a/app-id/t/canary-id/',
    })
    const selfHostedCanary = target({
      id: 'self-hosted-id',
      name: 'Self-hosted',
      slug: 'self-hosted',
      isDefault: false,
      routePath: '/a/app-id/t/self-hosted-id/',
      runner: {
        id: 'self-hosted-runner',
        name: 'Self-hosted Runner',
        purpose: 'Application',
        isHostedRunner: false,
        isOnline: true,
      },
    })
    const wrapper = await mountTargets([target(), hostedCanary, selfHostedCanary])

    for (const button of wrapper
      .findAll('button')
      .filter((candidate) => candidate.text().includes('Advanced'))) {
      await button.trigger('click')
    }

    expect(wrapper.text()).toContain('Runtime route: /private/app-id')
    expect(wrapper.text()).toContain('Runtime route: /a/app-id/t/canary-id/')
    expect(wrapper.text()).toContain('Runtime route: /a/app-id/t/self-hosted-id/')
  })

  it.each([
    target({ runner: null, status: DeploymentTargetStatus.Unassigned }),
    target({ frontendDeployed: false, status: DeploymentTargetStatus.NotDeployed }),
    target({
      runner: {
        id: 'runner-id',
        name: 'Offline',
        purpose: 'Application',
        isHostedRunner: false,
        isOnline: false,
      },
      status: DeploymentTargetStatus.RunnerOffline,
    }),
  ])('disables Open preview when a target is unavailable', async (unavailableTarget) => {
    const wrapper = await mountTargets([unavailableTarget])

    expect(buttonByText(wrapper, 'Open preview').attributes('disabled')).toBeDefined()
  })
})
