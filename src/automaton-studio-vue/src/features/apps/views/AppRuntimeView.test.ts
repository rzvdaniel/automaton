// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import ToastService from 'primevue/toastservice'
import AppRuntimeView from './AppRuntimeView.vue'

const writeText = vi.fn().mockResolvedValue(undefined)
Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true })

const mocks = vi.hoisted(() => ({
  get: vi.fn(),
  listDeploymentTargets: vi.fn(),
  createRuntimeLaunch: vi.fn(),
  canBeFramed: vi.fn(),
  getCatalogUpdate: vi.fn(),
  applyCatalogUpdate: vi.fn(),
}))

vi.mock('../services/apps.service', () => ({ appsService: mocks }))

function app(requiresAuthentication: boolean) {
  return { id: 'app-id', isPublished: false, requiresAuthentication } as never
}

async function mountView(requiresAuthentication: boolean, framable = true) {
  mocks.canBeFramed.mockResolvedValue(framable)
  mocks.get.mockResolvedValue(app(requiresAuthentication))
  mocks.listDeploymentTargets.mockResolvedValue([{ isDefault: true, genericRuntimeDeployed: true }])
  mocks.createRuntimeLaunch.mockResolvedValue({ runtimeToken: '' })
  const router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/apps/:id/application', component: AppRuntimeView }] })
  await router.push('/apps/app-id/application')
  await router.isReady()
  const wrapper = mount(AppRuntimeView, { global: { plugins: [router, ToastService], stubs: { ProgressSpinner: true } } })
  await flushPromises()
  return wrapper
}

describe('AppRuntimeView runtime bootstrap', () => {
  afterEach(() => vi.clearAllMocks())

  it('bootstraps authenticated runtime access before loading the canonical iframe URL', async () => {
    const wrapper = await mountView(true)
    const iframe = wrapper.get('iframe')

    expect(mocks.createRuntimeLaunch).toHaveBeenCalledWith('app-id')
    expect(iframe.attributes('src')).toMatch(/^https?:\/\/app-appid\..+\/?$/)
    expect(iframe.attributes('src')).not.toContain('?v=')
    expect(iframe.attributes('src')).not.toContain('/api/apps/app-id/launch-runtime')
  })

  it('renews the app session while the app stays open, and stops after unmount', async () => {
    vi.useFakeTimers()
    try {
      const wrapper = await mountView(true)
      mocks.createRuntimeLaunch.mockClear()
      await vi.advanceTimersByTimeAsync(4 * 60 * 1000)
      expect(mocks.createRuntimeLaunch).toHaveBeenCalledTimes(1)
      wrapper.unmount()
      await vi.advanceTimersByTimeAsync(8 * 60 * 1000)
      expect(mocks.createRuntimeLaunch).toHaveBeenCalledTimes(1)
    } finally {
      vi.useRealTimers()
    }
  })

  it('shows a note and an Open in a new tab button instead of a blank frame when the app forbids framing', async () => {
    const open = vi.spyOn(window, 'open').mockReturnValue(null)
    const wrapper = await mountView(true, false)

    expect(wrapper.find('iframe').exists()).toBe(false)
    expect(wrapper.text()).toContain('cannot be shown inside Studio')
    await wrapper.get('[data-testid="open-in-new-tab"]').trigger('click')
    expect(open).toHaveBeenCalledWith(expect.stringMatching(/^https?:\/\/app-appid\./), '_blank', 'noopener')
    open.mockRestore()
  })

  it('still hands out the session cookie when the app does not require sign-in, because its private address always asks for it', async () => {
    const wrapper = await mountView(false)

    expect(mocks.createRuntimeLaunch).toHaveBeenCalledWith('app-id')
    expect(wrapper.get('iframe').attributes('src')).not.toContain('/api/apps/app-id/launch-runtime')
  })

  it('shows the existing error state when authenticated bootstrap fails', async () => {
    mocks.get.mockResolvedValue(app(true))
    mocks.listDeploymentTargets.mockResolvedValue([{ isDefault: true, genericRuntimeDeployed: true }])
    mocks.createRuntimeLaunch.mockRejectedValue(new Error('bootstrap failed'))
    const router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/apps/:id/application', component: AppRuntimeView }] })
    await router.push('/apps/app-id/application')
    await router.isReady()
    const wrapper = mount(AppRuntimeView, { global: { plugins: [router, ToastService], stubs: { ProgressSpinner: true } } })
    await flushPromises()

    expect(wrapper.text()).toContain('bootstrap failed')
    expect(wrapper.find('iframe').exists()).toBe(false)
  })

  it('guides an undeployed app to the local CLI without changing Studio operations', async () => {
    mocks.get.mockResolvedValue(app(false))
    mocks.listDeploymentTargets.mockResolvedValue([{ isDefault: true, genericRuntimeDeployed: false }])
    const router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/apps/:id/application', component: AppRuntimeView }] })
    await router.push('/apps/app-id/application')
    await router.isReady()
    const wrapper = mount(AppRuntimeView, { global: { plugins: [router, ToastService], stubs: { ProgressSpinner: true } } })
    await flushPromises()

    expect(wrapper.text()).toContain('This app has not been deployed yet.')
    expect(wrapper.text()).toContain('automaton deploy')
    expect(wrapper.text()).toContain('Back to Studio')
    await wrapper.get('.copy-command__button').trigger('click')
    expect(writeText).toHaveBeenCalledWith('automaton deploy')
  })

  async function mountCatalogApp(update: unknown) {
    mocks.get.mockResolvedValue({ id: 'app-id', installedFromRepo: 'o/apps' } as never)
    mocks.listDeploymentTargets.mockResolvedValue([{ isDefault: true, genericRuntimeDeployed: false }])
    mocks.getCatalogUpdate.mockResolvedValue(update)
    mocks.applyCatalogUpdate.mockResolvedValue({})
    const router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/apps/:id/application', component: AppRuntimeView }, { path: '/apps/:id', component: { template: '<div />' } }] })
    await router.push('/apps/app-id/application')
    await router.isReady()
    const wrapper = mount(AppRuntimeView, { global: { plugins: [router, ToastService], stubs: { ProgressSpinner: true } } })
    await flushPromises()
    return { wrapper, router }
  }

  it('says the build failed and offers Retry and the log, not the CLI hint, for a failed catalog app', async () => {
    const { wrapper, router } = await mountCatalogApp({ available: true, installedCommit: 'a1', latestCommit: 'a1', edited: false, deployFailed: true, deployError: 'Build failed:\nTraceback\nValueError: bad pin' })
    expect(wrapper.text()).toContain('The build of this app failed.')
    expect(wrapper.get('[data-testid="runtime-failure"]').text()).toBe('Build failed: ValueError: bad pin')
    expect(wrapper.text()).not.toContain('automaton deploy')
    expect(wrapper.get('[data-testid="runtime-update"]').text()).toBe('Retry')
    await wrapper.get('[data-testid="runtime-update"]').trigger('click')
    await flushPromises()
    expect(mocks.applyCatalogUpdate).toHaveBeenCalledWith('app-id', false)
    expect(router.currentRoute.value.path).toBe('/apps/app-id')
    await router.push('/apps/app-id/application')
    await wrapper.get('[data-testid="runtime-log"]').trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.fullPath).toBe('/apps/app-id?view=activity')
  })

  it('offers Update for a catalog app that never deployed and has a newer version', async () => {
    const { wrapper } = await mountCatalogApp({ available: true, installedCommit: 'a1', latestCommit: 'b2', edited: false })
    expect(wrapper.get('[data-testid="runtime-update"]').text()).toBe('Update')
    expect(wrapper.text()).not.toContain('automaton deploy')
  })
})
