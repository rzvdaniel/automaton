// @vitest-environment jsdom
import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import PrimeVue from 'primevue/config'
import ToastService from 'primevue/toastservice'
import ConnectAiSection from './ConnectAiSection.vue'

function build() {
  return mount(ConnectAiSection, { global: { plugins: [PrimeVue, ToastService] } })
}

describe('ConnectAiSection', () => {
  it('shows three keyless steps for this server', () => {
    const w = build()
    expect(w.findAll('ol.steps > li')).toHaveLength(3)
    expect(w.get('[data-testid=claude-code-command]').text()).toBe(`claude mcp add --transport http automaton ${window.location.origin}/mcp`)
    expect(w.get('[data-testid=mcp-address]').text()).toBe(`${window.location.origin}/mcp`)
    expect(w.get('[data-testid=login-command]').text()).toBe(`automaton login --server ${window.location.origin}`)
    expect(w.text()).not.toContain('Bearer')
    expect(w.findAll('button').map((b) => b.text())).toEqual(['Copy', 'Copy', 'Copy'])
  })

  it('warns when the address is not https', () => {
    expect(build().find('[data-testid=https-note]').exists()).toBe(window.location.origin.startsWith('http:'))
  })

  it('opens the API keys tab from the script and CI link', async () => {
    const w = build()
    await w.get('[data-testid=use-key-link]').trigger('click')
    expect(w.emitted('open-keys')).toHaveLength(1)
  })
})
