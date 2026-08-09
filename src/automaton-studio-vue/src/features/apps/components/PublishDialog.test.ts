// @vitest-environment jsdom
import { describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import PublishDialog from './PublishDialog.vue'

vi.mock('@/shared/services/api/axios-instance', () => ({ default: { get: vi.fn().mockResolvedValue({ data: [] }) } }))
vi.mock('../services/apps.service', () => ({ appsService: { generateSlug: (name: string) => name.toLowerCase().replace(/\s+/g, '-') } }))

describe('PublishDialog', () => {
  it('makes Live publishing context explicit while a canary is being viewed', () => {
    const wrapper = mount(PublishDialog, {
      props: {
        visible: true,
        appName: 'Demo App',
        liveTargetName: 'hosted-control',
        liveTargetSlug: 'hosted-control',
        viewingTargetName: 'privileged-canary',
        viewingTargetSlug: 'privileged-canary',
      },
      global: {
        stubs: {
          Dialog: { template: '<div><h1>{{ header }}</h1><slot /><slot name="footer" /></div>', props: ['header'] },
          InputText: true,
          InputSwitch: true,
          Select: true,
        },
      },
    })

    expect(wrapper.text()).toContain('Publish Live')
    expect(wrapper.text()).toContain('Publishes the current Live environment.')
    expect(wrapper.text()).toContain('Additional environments remain private previews.')
    expect(wrapper.text()).toContain('Publishing Live: hosted-control')
    expect(wrapper.text()).toContain('Currently viewing: privileged-canary. Publishing still uses Live: hosted-control.')
    expect(wrapper.findAll('button').some((button) => button.text().includes('Publish Live'))).toBe(true)
  })
})
