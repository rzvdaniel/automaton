// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import PrimeVue from 'primevue/config'
import ToastService from 'primevue/toastservice'
import ConnectAiSection from './ConnectAiSection.vue'
import { useAuthStore } from '@/features/auth/stores/auth.store'

const create = vi.fn()
vi.mock('../services/apikeys.service', () => ({ apiKeysService: { create: (r: unknown) => create(r) } }))

function build(emailConfirmed = true) {
  const pinia = createPinia()
  setActivePinia(pinia)
  const auth = useAuthStore()
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  ;(auth as any).user = { emailConfirmed, roles: [] }
  return mount(ConnectAiSection, { global: { plugins: [pinia, PrimeVue, ToastService] } })
}

describe('ConnectAiSection', () => {
  beforeEach(() => create.mockReset())

  it('shows this server MCP address and a keyless command', () => {
    const w = build()
    expect(w.get('[data-testid=mcp-address]').text()).toBe(`${window.location.origin}/mcp`)
    const cmd = w.get('[data-testid=claude-code-command]').text()
    expect(cmd).toBe(`claude mcp add --transport http automaton ${window.location.origin}/mcp`)
    expect(cmd).not.toContain('Bearer')
    expect(w.find('[data-testid=key-section]').exists()).toBe(false)
    expect(w.find('button.p-button-secondary').exists()).toBe(false)
  })

  it('creates a Developer key only after "use an API key instead"', async () => {
    create.mockResolvedValue({ apiKey: 'amk_secret' })
    const w = build()
    await w.get('[data-testid=use-key-link]').trigger('click')
    expect(w.get('[data-testid=claude-code-command]').text()).toContain('<your API key>')
    await w.get('button.p-button-secondary').trigger('click')
    await flushPromises()
    expect(create.mock.calls[0]?.[0].scope).toBe(7)
    expect(w.get('[data-testid=claude-code-command]').text()).toContain('Bearer amk_secret')
    expect(w.find('[data-testid=key-once]').exists()).toBe(true)
  })

  it('disables key creation until the email is confirmed', async () => {
    const w = build(false)
    await w.get('[data-testid=use-key-link]').trigger('click')
    expect(w.get('button.p-button-secondary').attributes('disabled')).toBeDefined()
  })
})
