// @vitest-environment jsdom
import { mount } from '@vue/test-utils'
import PrimeVue from 'primevue/config'
import { describe, expect, it } from 'vitest'
import NewAppDialog from './NewAppDialog.vue'

const mountDialog = () =>
  mount(NewAppDialog, {
    props: { visible: true },
    global: { plugins: [PrimeVue], stubs: { Dialog: { template: '<div><slot /></div>' }, RouterLink: { template: '<a><slot /></a>' } } },
  })

describe('NewAppDialog', () => {
  it('offers the AI prompt, CLI login and the catalog', () => {
    const text = mountDialog().text()
    expect(text).toContain('Ask your AI')
    expect(text).toContain('automaton login')
    expect(text).toContain('Pick from the catalog')
  })

  it('points the prompt at the current server, not automaton.monster', () => {
    const text = mountDialog().text()
    expect(text).toContain(`${window.location.origin}/llms.txt`)
    expect(text).not.toContain('automaton.monster/llms.txt')
  })

  it('closes and asks for the catalog', async () => {
    const wrapper = mountDialog()
    const button = wrapper.findAll('button').find((b) => b.text().includes('Open the catalog'))!
    await button.trigger('click')
    await wrapper.vm.$nextTick()
    expect(wrapper.emitted('catalog')).toHaveLength(1)
    expect(wrapper.emitted('update:visible')?.[0]).toEqual([false])
  })
})
