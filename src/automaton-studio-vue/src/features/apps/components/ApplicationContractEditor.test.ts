// @vitest-environment jsdom
import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import PrimeVue from 'primevue/config'
import ApplicationContractEditor from './ApplicationContractEditor.vue'
import { createDefaultApplicationContract, type ApplicationContract } from '../types/apps.types'

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

  it('commits multiline command and environment input without destroying typed text', async () => {
    const wrapper = mount(ApplicationContractEditor, {
      props: {
        modelValue: {
          ...createDefaultApplicationContract(),
          runtime: {
            command: { executable: 'server', arguments: [], workingDirectory: '.', environment: {}, useShell: false },
            port: 8080,
          },
        },
      },
      global: { plugins: [PrimeVue] },
    })

    const argumentsInput = wrapper.find('#runtime-arguments')
    await argumentsInput.setValue('first\nsecond\n')
    expect((argumentsInput.element as HTMLTextAreaElement).value).toBe('first\nsecond\n')
    expect(wrapper.emitted('update:modelValue')).toBeUndefined()
    await argumentsInput.trigger('blur')

    const environmentInput = wrapper.find('#runtime-environment')
    await environmentInput.setValue('KEY=value=with=equals\nSECOND=two')
    await environmentInput.trigger('blur')

    const updates = wrapper.emitted('update:modelValue') ?? []
    const contract = updates[updates.length - 1]?.[0] as ApplicationContract
    expect(contract.runtime.command?.arguments).toEqual(['first', 'second'])
    expect(contract.runtime.command?.environment).toEqual({ KEY: 'value=with=equals', SECOND: 'two' })
  })

  it('preserves shell mode and storage mounts while editing visible fields', async () => {
    const modelValue = {
      ...createDefaultApplicationContract(),
      runtime: {
        command: { executable: 'server', arguments: ['--port', '8080'], workingDirectory: '.', environment: { EXISTING: 'value=kept' }, useShell: true },
        port: 8080,
      },
      storage: {
        dataDirectory: 'data',
        mounts: [{ name: 'uploads', relativePath: 'data/uploads', readOnly: false }],
      },
    }
    const wrapper = mount(ApplicationContractEditor, {
      props: { modelValue },
      global: { plugins: [PrimeVue] },
    })

    expect(wrapper.find('#runtime-use-shell').exists()).toBe(true)
    expect((wrapper.find('#storage-mount-name-0').element as HTMLInputElement).value).toBe('uploads')
    await wrapper.find('#runtime-arguments').trigger('blur')

    const updates = wrapper.emitted('update:modelValue') ?? []
    const contract = updates[updates.length - 1]?.[0] as typeof modelValue
    expect(contract.runtime.command?.useShell).toBe(true)
    expect(contract.runtime.command?.arguments).toEqual(['--port', '8080'])
    expect(contract.runtime.command?.environment).toEqual({ EXISTING: 'value=kept' })
    expect(contract.storage.mounts).toEqual([{ name: 'uploads', relativePath: 'data/uploads', readOnly: false }])
  })
})
