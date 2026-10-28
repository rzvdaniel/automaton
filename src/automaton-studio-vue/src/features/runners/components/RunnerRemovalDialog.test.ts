// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import RunnerRemovalDialog from './RunnerRemovalDialog.vue'
import { runnersService } from '../services/runners.service'

vi.mock('../services/runners.service', () => ({ runnersService: { delete: vi.fn() } }))

function mountDialog() {
  return mount(RunnerRemovalDialog, {
    props: { visible: true, runnerName: 'Tasks server',
      instructions: { runnerId: 'runner-1', uninstallCommand: 'echo installation-specific-command' } },
    global: {
      stubs: {
        Dialog: { template: '<div><slot /><slot name="footer" /></div>' },
        Message: { template: '<div role="alert"><slot /></div>' },
        Textarea: { props: ['modelValue'], template: '<textarea readonly :value="modelValue" />' },
        Checkbox: { props: ['modelValue'], emits: ['update:modelValue'],
          template: '<input type="checkbox" :checked="modelValue" @change="$emit(\'update:modelValue\', $event.target.checked)" />' },
        Button: { props: ['label', 'disabled', 'loading'], emits: ['click'],
          template: '<button :disabled="disabled || loading" @click="$emit(\'click\')">{{ label }}</button>' },
      },
    },
  })
}

describe('RunnerRemovalDialog', () => {
  beforeEach(() => vi.resetAllMocks())

  it('requires explicit host cleanup confirmation before removing registration', async () => {
    const wrapper = mountDialog()
    const confirm = wrapper.findAll('button').find(button => button.text() === 'Confirm removal')!
    expect(confirm.attributes('disabled')).toBeDefined()
    expect(wrapper.text()).toContain('New app assignments are blocked')
    expect(wrapper.get('textarea').element.value).toContain('installation-specific-command')
    await wrapper.get('input[type="checkbox"]').setValue(true)
    expect(confirm.attributes('disabled')).toBeUndefined()
    await confirm.trigger('click')
    await flushPromises()
    expect(runnersService.delete).toHaveBeenCalledWith('runner-1')
    expect(wrapper.emitted('removed')).toHaveLength(1)
  })

  it('keeps the removal dialog open and shows the API cleanup failure for retry', async () => {
    vi.mocked(runnersService.delete).mockRejectedValue({ isAxiosError: true,
      response: { data: { message: 'The Runner is still online.' } } })
    const wrapper = mountDialog()
    await wrapper.get('input[type="checkbox"]').setValue(true)
    await wrapper.findAll('button').find(button => button.text() === 'Confirm removal')!.trigger('click')
    await flushPromises()
    expect(wrapper.text()).toContain('The Runner is still online.')
    expect(wrapper.emitted('removed')).toBeUndefined()
    expect(wrapper.emitted('update:visible')).toBeUndefined()
  })
})
