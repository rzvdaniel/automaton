// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import CopyCommand from './CopyCommand.vue'

const writeText = vi.fn()

describe('CopyCommand', () => {
  beforeEach(() => {
    writeText.mockResolvedValue(undefined)
    Object.defineProperty(navigator, 'clipboard', {
      configurable: true,
      value: { writeText },
    })
  })

  afterEach(() => vi.clearAllMocks())

  it('renders and copies the exact command with accessible feedback', async () => {
    const wrapper = mount(CopyCommand, { props: { command: 'automaton deploy live' } })

    expect(wrapper.get('code').text()).toBe('automaton deploy live')
    await wrapper.get('button').trigger('click')

    expect(writeText).toHaveBeenCalledWith('automaton deploy live')
    expect(wrapper.get('button').text()).toContain('Copied')
    expect(wrapper.get('[role="status"]').text()).toBe('Command copied to clipboard.')
  })

  it('reports a clipboard failure instead of claiming the command was copied', async () => {
    writeText.mockRejectedValue(new Error('clipboard unavailable'))
    const wrapper = mount(CopyCommand, { props: { command: 'automaton deploy' } })

    await wrapper.get('button').trigger('click')

    expect(wrapper.get('button').text()).toContain('Copy failed')
    expect(wrapper.get('[role="status"]').text()).toBe('Could not copy command.')
  })
})
