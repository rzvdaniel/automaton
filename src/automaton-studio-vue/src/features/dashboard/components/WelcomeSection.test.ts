// @vitest-environment jsdom
import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import PrimeVue from 'primevue/config'
import Tooltip from 'primevue/tooltip'
import WelcomeSection from './WelcomeSection.vue'

describe('WelcomeSection', () => {
  it('assigns source creation to the local CLI workflow and operations to Studio', async () => {
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/', component: { template: '<div />' } }],
    })
    await router.push('/')
    await router.isReady()

    const wrapper = mount(WelcomeSection, {
      props: {
        hasApiKeys: false,
        hasRunners: false,
        hasApps: false,
        hasPublishedApp: false,
      },
      global: {
        plugins: [router, PrimeVue],
        directives: { tooltip: Tooltip },
      },
    })

    expect(wrapper.text()).toContain('automaton new')
    expect(wrapper.text()).toContain('deploy with the CLI')
    expect(wrapper.text()).toContain('operate apps in Studio')
    expect(wrapper.text()).not.toContain('manage apps from your machine')
  })
})
