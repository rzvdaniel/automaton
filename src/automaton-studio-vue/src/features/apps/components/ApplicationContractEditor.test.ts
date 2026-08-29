// @vitest-environment jsdom
import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import PrimeVue from 'primevue/config'
import ApplicationContractEditor from './ApplicationContractEditor.vue'
import { createDefaultApplicationContract } from '../types/apps.types'

describe('ApplicationContractEditor', () => {
  it('exposes the canonical stack-neutral build, runtime, health, and storage concepts', () => {
    const wrapper = mount(ApplicationContractEditor, {
      props: { modelValue: createDefaultApplicationContract() },
      global: { plugins: [PrimeVue] },
    })

    expect(wrapper.text()).toContain('Build')
    expect(wrapper.text()).toContain('Runtime')
    expect(wrapper.text()).toContain('Health path')
    expect(wrapper.text()).toContain('Persistent data directory')
    expect(wrapper.text()).not.toContain('frontend')
    expect(wrapper.text()).not.toContain('backend')
  })
})
