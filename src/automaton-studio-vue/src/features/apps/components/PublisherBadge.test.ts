// @vitest-environment jsdom
import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import PublisherBadge from './PublisherBadge.vue'

describe('PublisherBadge', () => {
  it('shows "by <publisher>" and the logo', () => {
    const wrapper = mount(PublisherBadge, { props: { publisher: { name: 'Acme', logo: 'https://example.com/a.svg', verified: false } } })
    expect(wrapper.text()).toBe('by Acme')
    expect(wrapper.find('img').attributes('src')).toBe('https://example.com/a.svg')
    expect(wrapper.find('[data-testid="publisher-verified"]').exists()).toBe(false)
  })

  it('shows a verified tick for a verified publisher and no logo when there is none', () => {
    const wrapper = mount(PublisherBadge, { props: { publisher: { name: 'Automaton', verified: true } } })
    expect(wrapper.text()).toBe('by Automaton')
    expect(wrapper.find('img').exists()).toBe(false)
    expect(wrapper.find('[data-testid="publisher-verified"]').attributes('aria-label')).toBe('Verified publisher')
  })
})
