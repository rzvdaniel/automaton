// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import ShareDialog from './ShareDialog.vue'
import { appsService } from '../services/apps.service'

vi.mock('../services/apps.service', () => ({
  appsService: {
    generateSlug: (name: string) => name.toLowerCase().replace(/\s+/g, '-'),
    getSharing: vi.fn(),
    setSharing: vi.fn(),
    addSharedPerson: vi.fn(),
    removeSharedPerson: vi.fn(),
  },
}))

const stubs = {
  Dialog: { template: '<div><slot /><slot name="footer" /></div>' },
  Message: { template: '<div class="msg"><slot /></div>' },
  InputText: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template: `<input :value="modelValue" @input="$emit('update:modelValue', $event.target.value)">`,
  },
}

function open() {
  return mount(ShareDialog, { props: { visible: true, appId: 'app-1', appName: 'Static Tool' }, global: { stubs } })
}

describe('ShareDialog', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(appsService.getSharing).mockResolvedValue({ audience: 'me', people: [] })
  })

  it('offers the four choices and starts on the current one', async () => {
    const wrapper = open()
    await flushPromises()

    const text = wrapper.text()
    for (const title of ['Only me', 'People I choose', 'Everyone on this server', 'Anyone on the internet']) {
      expect(text).toContain(title)
    }
    expect((wrapper.find('[data-testid="share-choice-me"] input').element as HTMLInputElement).checked).toBe(true)
    expect(wrapper.find('[data-testid="share-save"]').attributes('disabled')).toBeDefined()
  })

  it('saves Everyone on this server and tells the page', async () => {
    vi.mocked(appsService.setSharing).mockResolvedValue({ audience: 'server', people: [] })
    const wrapper = open()
    await flushPromises()

    await wrapper.find('[data-testid="share-choice-server"] input').setValue(true)
    await wrapper.find('[data-testid="share-save"]').trigger('click')
    await flushPromises()

    expect(appsService.setSharing).toHaveBeenCalledWith('app-1', 'server', undefined)
    expect(wrapper.emitted('changed')).toHaveLength(1)
  })

  it('needs a valid public address before Anyone on the internet can be saved', async () => {
    vi.mocked(appsService.setSharing).mockResolvedValue({ audience: 'internet', people: [], publicUrlSlug: 'static-tool' })
    const wrapper = open()
    await flushPromises()

    await wrapper.find('[data-testid="share-choice-internet"] input').setValue(true)
    expect(wrapper.find('[data-testid="share-internet"]').text()).toContain('Anyone with the address')
    await wrapper.find('#share-address').setValue('A!')
    expect(wrapper.find('[data-testid="share-save"]').attributes('disabled')).toBeDefined()

    await wrapper.find('#share-address').setValue('static-tool')
    await wrapper.find('[data-testid="share-save"]').trigger('click')
    await flushPromises()

    expect(appsService.setSharing).toHaveBeenCalledWith('app-1', 'internet', 'static-tool')
  })

  it('adds and removes people and shows the server message when a person is unknown', async () => {
    const person = { userId: 'u-1', email: 'ana@example.test', displayName: 'Ana' }
    vi.mocked(appsService.addSharedPerson)
      .mockRejectedValueOnce({ response: { data: { message: 'Nobody on this server uses that email address.' } } })
      .mockResolvedValueOnce({ audience: 'people', people: [person] })
    vi.mocked(appsService.removeSharedPerson).mockResolvedValue({ audience: 'me', people: [] })
    const wrapper = open()
    await flushPromises()
    await wrapper.find('[data-testid="share-choice-people"] input').setValue(true)

    await wrapper.find('input[type="email"]').setValue('nobody@example.test')
    await wrapper.find('form').trigger('submit')
    await flushPromises()
    expect(wrapper.find('[data-testid="share-error"]').text()).toContain('Nobody on this server')

    await wrapper.find('input[type="email"]').setValue('ana@example.test')
    await wrapper.find('form').trigger('submit')
    await flushPromises()
    expect(wrapper.find('[data-testid="share-people"]').text()).toContain('ana@example.test')

    await wrapper.find('[aria-label="Stop sharing with ana@example.test"]').trigger('click')
    await flushPromises()
    expect(appsService.removeSharedPerson).toHaveBeenCalledWith('app-1', 'u-1')
    expect(wrapper.find('[data-testid="share-people"]').text()).not.toContain('ana@example.test')
  })
})
