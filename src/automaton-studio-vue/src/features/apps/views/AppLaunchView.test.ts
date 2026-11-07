// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import AppLaunchView from './AppLaunchView.vue'

const mocks = vi.hoisted(() => ({ createRuntimeLaunch: vi.fn() }))
vi.mock('../services/apps.service', () => ({ appsService: mocks }))

const replace = vi.fn()

async function mountView(query: string) {
  Object.defineProperty(window, 'location', {
    value: { origin: 'https://studio.test', replace },
    configurable: true,
  })
  const router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/apps/:id/launch', component: AppLaunchView }, { path: '/apps', component: { template: '<div />' } }] })
  await router.push(`/apps/11111111-2222-3333-4444-555555555555/launch${query}`)
  await router.isReady()
  const wrapper = mount(AppLaunchView, { global: { plugins: [router], stubs: { ProgressSpinner: true } } })
  await flushPromises()
  return wrapper
}

describe('AppLaunchView', () => {
  afterEach(() => vi.clearAllMocks())

  it('hands out the session cookie, then returns the visitor to the page they asked for on the private address', async () => {
    mocks.createRuntimeLaunch.mockResolvedValue({ runtimeToken: '' })
    await mountView('?to=%2Fboard%3Fx%3D1')

    expect(mocks.createRuntimeLaunch).toHaveBeenCalledWith('11111111-2222-3333-4444-555555555555')
    const target = new URL(replace.mock.calls[0]![0] as string)
    expect(target.host).toMatch(/^app-11111111222233334444555555555555\./)
    expect(target.pathname + target.search).toBe('/board?x=1')
  })

  it('never leaves the app address, even for a return path that names another host', async () => {
    mocks.createRuntimeLaunch.mockResolvedValue({ runtimeToken: '' })
    await mountView('?to=%2F%2Fevil.test%2Fx')

    const target = new URL(replace.mock.calls[0]![0] as string)
    expect(target.host).toMatch(/^app-/)
    expect(target.pathname).toBe('/')
  })

  it.each(['/\\evil.test/x', '/\t/evil.test', 'https://evil.test/x'])('never leaves the app address for %j', async (path) => {
    mocks.createRuntimeLaunch.mockResolvedValue({ runtimeToken: '' })
    await mountView(`?to=${encodeURIComponent(path)}`)

    const target = new URL(replace.mock.calls[0]![0] as string)
    expect(target.host).toMatch(/^app-/)
  })

  it('tells a signed-in user without access that the app is not theirs', async () => {
    mocks.createRuntimeLaunch.mockRejectedValue({ response: { status: 403 } })
    const wrapper = await mountView('')

    expect(replace).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('You do not have access to this app')
  })
})
