// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import PrimeVue from 'primevue/config'
import ToastService from 'primevue/toastservice'
import { createMemoryHistory, createRouter } from 'vue-router'
import { DeploymentTargetStatus, type AppDeploymentTarget } from '../types/apps.types'
import { useAppEditorStore } from '../stores/app-editor.store'
import BuildVisualWorkspace from './BuildVisualWorkspace.vue'

function target(overrides: Partial<AppDeploymentTarget> = {}): AppDeploymentTarget {
  return {
    id: 'live-target-id', appId: 'app-id', name: 'Live', slug: 'live', isDefault: true,
    status: DeploymentTargetStatus.Running, frontendDeployed: true, backendDeployed: true,
    cleanupPending: false, routePath: '/private/app-id/live',
    runner: { id: 'runner-id', name: 'Hosted', purpose: 'Application', isHostedRunner: true, isOnline: true },
    ...overrides,
  }
}

function mountWorkspace(targets: AppDeploymentTarget[], selectedTarget = targets[0] ?? null) {
  const pinia = createPinia()
  const router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/', component: { template: '<div />' } }] })
  setActivePinia(pinia)
  const store = useAppEditorStore()
  store.currentApp = { id: 'app-id', name: 'App' } as never

  return mount(BuildVisualWorkspace, {
    props: { targets, selectedTarget },
    global: {
      plugins: [pinia, router, PrimeVue, ToastService],
      stubs: {
        Splitter: { template: '<div><slot /></div>' },
        SplitterPanel: { template: '<div><slot /></div>' },
        ConsolePanel: true,
      },
      directives: { tooltip: {} },
    },
  })
}

describe('BuildVisualWorkspace target preview', () => {
  beforeEach(() => {
    vi.stubGlobal('open', vi.fn())
    Object.assign(navigator, { clipboard: { writeText: vi.fn().mockResolvedValue(undefined) } })
  })

  it('renders one target as a non-interactive summary', () => {
    const wrapper = mountWorkspace([target()])

    expect(wrapper.find('select[aria-label="Environment"]').exists()).toBe(false)
    expect(wrapper.text()).toContain('Live · Automaton Hosted · Running')
    expect(wrapper.text()).toContain('Workspace source · Environment: Live')
  })

  it('renders multiple targets and emits the selected target', async () => {
    const live = target()
    const canary = target({ id: 'canary-target-id', name: 'Canary', slug: 'canary', isDefault: false })
    const wrapper = mountWorkspace([live, canary])

    const selector = wrapper.get('select[aria-label="Environment"]')
    expect(selector.findAll('option')).toHaveLength(2)
    await selector.setValue('canary')
    expect(wrapper.emitted('selectTarget')?.[0]).toEqual([canary])
  })

  it('keeps technical slugs out of the normal selector and hides a disposable legacy default', () => {
    const live = target()
    const canary = target({ id: 'canary-target-id', name: 'privileged-canary', slug: 'privileged-canary', isDefault: false,
      runner: { id: 'runner-id', name: 'Privileged', purpose: 'Application', applicationProfile: 'PrivilegedHost', isHostedRunner: false, isOnline: true } })
    const legacy = target({ id: 'legacy-target-id', name: 'Default', slug: 'default', isDefault: false,
      runner: null, frontendDeployed: false, backendDeployed: false, status: DeploymentTargetStatus.Unassigned })
    const wrapper = mountWorkspace([live, canary, legacy], live)

    const options = wrapper.findAll('option')
    expect(options).toHaveLength(2)
    expect(options[0]?.text()).toContain('Live')
    expect(options[0]?.text()).not.toContain('hosted-control')
    expect(options[1]?.text()).toContain('Canary')
    expect(options[1]?.text()).toContain('Privileged Host')
  })

  it('uses a stable target URL for Open and Copy and reserves cache busting for the iframe', async () => {
    const wrapper = mountWorkspace([target()])
    const iframe = wrapper.get('iframe.application-iframe')
    const iframeUrl = iframe.attributes('src')

    expect(iframeUrl).toContain('/private/app-id/live')
    expect(iframeUrl).toContain('?v=')

    await wrapper.get('button').trigger('click')
    expect(window.open).toHaveBeenCalledWith(
      expect.stringContaining('/private/app-id/live'), '_blank', 'noopener',
    )
    const openedUrl = vi.mocked(window.open).mock.calls[0]?.[0]
    expect(openedUrl).toBeDefined()
    expect(openedUrl).not.toContain('?v=')

    await wrapper.get('button:nth-of-type(2)').trigger('click')
    expect(navigator.clipboard.writeText).toHaveBeenCalledWith(expect.stringContaining('/private/app-id/live'))
    const copiedUrl = vi.mocked(navigator.clipboard.writeText).mock.calls[0]?.[0]
    expect(copiedUrl).toBeDefined()
    expect(copiedUrl).not.toContain('?v=')
  })

  it.each([
    target({ frontendDeployed: false, status: DeploymentTargetStatus.NotDeployed }),
    target({ runner: { id: 'runner-id', name: 'Offline', purpose: 'Application', isHostedRunner: false, isOnline: false }, status: DeploymentTargetStatus.RunnerOffline }),
  ])('does not render an iframe for unavailable targets', (unavailableTarget) => {
    const wrapper = mountWorkspace([unavailableTarget])

    expect(wrapper.find('iframe.application-iframe').exists()).toBe(false)
  })
})
