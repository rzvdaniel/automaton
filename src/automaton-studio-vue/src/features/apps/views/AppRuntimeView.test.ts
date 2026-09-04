// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import AppRuntimeView from './AppRuntimeView.vue'

const mocks = vi.hoisted(() => ({
  get: vi.fn(),
  listDeploymentTargets: vi.fn(),
  createRuntimeLaunch: vi.fn(),
  getRestrictedRuntimeLaunchUrl: vi.fn(() => '/api/apps/app-id/launch-runtime'),
}))

vi.mock('../services/apps.service', () => ({ appsService: mocks }))

function app(requiresAuthentication: boolean) {
  return { id: 'app-id', isPublished: false, requiresAuthentication } as never
}

async function mountView(requiresAuthentication: boolean) {
  mocks.get.mockResolvedValue(app(requiresAuthentication))
  mocks.listDeploymentTargets.mockResolvedValue([{ isDefault: true, genericRuntimeDeployed: true }])
  mocks.createRuntimeLaunch.mockResolvedValue({ runtimeToken: '' })
  const router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/apps/:id/application', component: AppRuntimeView }] })
  await router.push('/apps/app-id/application')
  await router.isReady()
  const wrapper = mount(AppRuntimeView, { global: { plugins: [router], stubs: { ProgressSpinner: true } } })
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

  it('keeps unprotected runtime loading direct without bootstrap', async () => {
    const wrapper = await mountView(false)

    expect(mocks.createRuntimeLaunch).not.toHaveBeenCalled()
    expect(wrapper.get('iframe').attributes('src')).not.toContain('/api/apps/app-id/launch-runtime')
  })

  it('shows the existing error state when authenticated bootstrap fails', async () => {
    mocks.get.mockResolvedValue(app(true))
    mocks.listDeploymentTargets.mockResolvedValue([{ isDefault: true, genericRuntimeDeployed: true }])
    mocks.createRuntimeLaunch.mockRejectedValue(new Error('bootstrap failed'))
    const router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/apps/:id/application', component: AppRuntimeView }] })
    await router.push('/apps/app-id/application')
    await router.isReady()
    const wrapper = mount(AppRuntimeView, { global: { plugins: [router], stubs: { ProgressSpinner: true } } })
    await flushPromises()

    expect(wrapper.text()).toContain('bootstrap failed')
    expect(wrapper.find('iframe').exists()).toBe(false)
  })
})
