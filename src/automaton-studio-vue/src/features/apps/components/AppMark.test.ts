// @vitest-environment jsdom
import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import AppMark from './AppMark.vue'

describe('AppMark', () => {
  it('shows the initial on a palette colour that is the same on every mount', () => {
    const first = mount(AppMark, { props: { id: 'abc', name: 'notes' } })
    const second = mount(AppMark, { props: { id: 'abc', name: 'notes' } })
    expect(first.text()).toBe('N')
    expect(first.attributes('style')).toBe(second.attributes('style'))
    expect(first.attributes('style')).toContain('border-radius: 11px')
  })

  it('shows the image when the app has one', () => {
    const wrapper = mount(AppMark, { props: { id: 'abc', name: 'notes', imageUrl: 'https://x.test/a.png' } })
    expect(wrapper.find('img').attributes('src')).toBe('https://x.test/a.png')
    expect(wrapper.text()).toBe('')
  })
})
