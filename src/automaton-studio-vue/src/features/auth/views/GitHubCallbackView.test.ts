// @vitest-environment jsdom
import { describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import PrimeVue from 'primevue/config'
import { createMemoryHistory, createRouter } from 'vue-router'
import GitHubCallbackView from './GitHubCallbackView.vue'

vi.mock('../stores/auth.store', () => ({ useAuthStore: () => ({ clearError: vi.fn() }) }))
vi.mock('../services/auth.service', () => ({ authService: { startGitHubLogin: vi.fn() } }))

async function mountError(error: string) {
  const router = createRouter({ history: createMemoryHistory(), routes: [
    { path: '/auth/github/callback', component: GitHubCallbackView },
  ] })
  await router.push({ path: '/auth/github/callback', query: { error } })
  const wrapper = mount(GitHubCallbackView, { global: { plugins: [router, PrimeVue] } })
  await flushPromises()
  return wrapper
}

describe('GitHub registration pause', () => {
  it('shows the exact pause message and sign-in navigation without suggesting a retry', async () => {
    const message = 'Public registration is temporarily paused while we prepare Automaton for wider access.'
    const wrapper = await mountError(message)
    expect(wrapper.text()).toContain(message)
    expect(wrapper.text()).toContain('Public registration paused')
    expect(wrapper.text()).toContain('Back to Sign In')
    expect(wrapper.text()).not.toContain('Try GitHub Again')
  })
  it('keeps unrecognized OAuth errors generic', async () => {
    const wrapper = await mountError('Untrusted provider detail')
    expect(wrapper.text()).not.toContain('Untrusted provider detail')
    expect(wrapper.text()).toContain('Try GitHub Again')
  })
})
