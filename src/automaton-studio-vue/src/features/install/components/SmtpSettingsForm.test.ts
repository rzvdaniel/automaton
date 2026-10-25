// @vitest-environment jsdom
import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import SmtpSettingsForm from './SmtpSettingsForm.vue'
import { smtpSettingsService } from '../services/smtp-settings.service'
import type { SmtpSettingsSummary } from '../types/install.types'

vi.mock('../services/smtp-settings.service', () => ({ smtpSettingsService: {
  get: vi.fn(), save: vi.fn(), verify: vi.fn(), sendTest: vi.fn(),
} }))

const current: SmtpSettingsSummary = { source: 'studio', emailHost: 'smtp.example.test', emailPort: 587,
  emailUseSsl: true, emailAccount: 'sender@example.test', fromEmail: 'sender@example.test',
  supportEmail: 'support@example.test', passwordSet: true, verifiedAt: '2026-10-25T10:00:00Z' }

const options = { global: { stubs: {
  Button: { props: ['label', 'disabled'], template: '<button :disabled="disabled" @click="$emit(\'click\')">{{ label }}</button>' },
  Checkbox: true, InputNumber: true,
  InputText: { props: ['modelValue', 'id'], template: '<input :id="id" :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)" />' },
  Password: true,
  Message: { template: '<div><slot /></div>' },
  Tag: { props: ['value'], template: '<span>{{ value }}</span>' },
} } }

describe('SMTP settings form', () => {
  it('clears verification after saving without returning the password', async () => {
    vi.mocked(smtpSettingsService.get)
      .mockResolvedValueOnce(current).mockResolvedValueOnce({ ...current, verifiedAt: null })
    vi.mocked(smtpSettingsService.save).mockResolvedValue()
    const wrapper = mount(SmtpSettingsForm, options)
    await flushPromises()
    expect(wrapper.text()).toContain('Authentication verified')
    await wrapper.get('form').trigger('submit')
    await flushPromises()
    expect(smtpSettingsService.save).toHaveBeenCalledWith(expect.objectContaining({ emailPassword: '' }))
    expect(wrapper.text()).toContain('Authentication unverified')
    expect(wrapper.text()).not.toContain('new-password')
  })

  it('shows a decryption error without offering the editor', async () => {
    vi.mocked(smtpSettingsService.get).mockRejectedValue({ isAxiosError: true,
      response: { data: { message: 'Saved SMTP settings cannot be decrypted.' } } })
    const wrapper = mount(SmtpSettingsForm, options)
    await flushPromises()
    expect(wrapper.text()).toContain('Saved SMTP settings cannot be decrypted.')
    expect(wrapper.find('form').exists()).toBe(false)
  })

  it('sends a test message only after a recipient is supplied', async () => {
    vi.mocked(smtpSettingsService.get).mockResolvedValue(current)
    vi.mocked(smtpSettingsService.sendTest).mockResolvedValue()
    const wrapper = mount(SmtpSettingsForm, options)
    await flushPromises()
    const button = wrapper.findAll('button').find(item => item.text() === 'Send test email')!
    expect(button.attributes('disabled')).toBeDefined()
    await wrapper.get('#smtp-test-recipient').setValue('owner@example.test')
    await button.trigger('click')
    await flushPromises()
    expect(smtpSettingsService.sendTest).toHaveBeenCalledWith('owner@example.test')
    expect(wrapper.text()).toContain('SMTP server accepted the test message')
  })
})
