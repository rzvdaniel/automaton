// @vitest-environment jsdom
import { mount, flushPromises } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import SetupChecks from './SetupChecks.vue'
import { installService } from '../services/install.service'
vi.mock('../services/install.service', () => ({ installService: { check: vi.fn() } }))
const options = { global: { stubs: {
  Button: { template: '<button @click="$emit(\'click\')">Check installation</button>' },
  Message: { template: '<div><slot /></div>' },
  Tag: true,
} } }
describe('installation checks', () => {
  it('does not mark an installation ready when a dependency fails', async () => {
    vi.mocked(installService.check).mockResolvedValue([{ name: 'Database', status: 'error', detail: 'Connection failed.' }])
    const wrapper = mount(SetupChecks, options)
    await wrapper.get('button').trigger('click')
    await flushPromises()
    expect(wrapper.emitted('ready')?.slice(-1)[0]).toEqual([false])
    expect(wrapper.text()).toContain('Connection failed.')
  })
  it('discards a response when its setup token changes during the check', async () => {
    let resolve!: (value: Awaited<ReturnType<typeof installService.check>>) => void
    vi.mocked(installService.check).mockReturnValue(new Promise(done => { resolve = done }))
    const wrapper = mount(SetupChecks, { ...options, props: { setupToken: 'old' } })
    await wrapper.get('button').trigger('click')
    await wrapper.setProps({ setupToken: 'new' })
    resolve([{ name: 'Database', status: 'ready', detail: 'Connected.' }])
    await flushPromises()
    expect(wrapper.emitted('ready')?.slice(-1)[0]).toEqual([false])
    expect(wrapper.text()).not.toContain('Connected.')
  })
})
