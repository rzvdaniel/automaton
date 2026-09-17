// @vitest-environment jsdom
import { describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import PublishDialog from './PublishDialog.vue'

vi.mock('../services/apps.service', () => ({ appsService: { generateSlug: (name: string) => name.toLowerCase().replace(/\s+/g, '-') } }))

describe('PublishDialog', () => {
  it('makes Live publishing context explicit while a canary is being viewed', () => {
    const wrapper = mount(PublishDialog, {
      props: {
        visible: true,
        appName: 'Demo App',
        hasLiveTarget: true,
        liveTargetName: 'Live',
        liveTargetSlug: 'hosted-control',
        viewingTargetName: 'Canary',
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

    expect(wrapper.text()).toContain('Publishing settings')
    expect(wrapper.text()).toContain('Publishing controls public access and marketplace visibility.')
    expect(wrapper.text()).toContain('It does not deploy or update the Live runtime.')
    expect(wrapper.text()).toContain('Additional environments remain private previews.')
    expect(wrapper.text()).toContain('Currently viewing: Canary. Additional environments remain private previews.')
    expect(wrapper.text()).not.toContain('hosted-control')
    expect(wrapper.text()).not.toContain('privileged-canary')
    expect(wrapper.text()).not.toContain('Marketplace Category')
    expect(wrapper.text()).not.toContain('Tags')
    expect(wrapper.text()).not.toContain('Access Control')
    expect(wrapper.findAll('button').some((button) => button.text().includes('Publish app'))).toBe(true)
  })

  it('preserves required sign-in and prevents an invalid public-runtime request', async () => {
    const wrapper = mount(PublishDialog, {
      props: {
        visible: true,
        appName: 'Restricted App',
        requiresAuth: true,
        currentRuntimeVisibility: 'public',
      },
      global: {
        stubs: {
          Dialog: { template: '<div><slot /><slot name="footer" /></div>' },
          InputText: true,
          InputSwitch: true,
          Message: true,
        },
      },
    })

    expect(wrapper.text()).toContain('This app requires sign-in, so its runtime must remain private.')
    expect(wrapper.text()).toContain('Sign-in is currently required. This dialog preserves that setting and keeps the runtime private.')
    expect(wrapper.text()).toContain('Private runtime')

    const publish = wrapper.findAll('button').find((button) => button.text().includes('Publish app'))
    if (!publish) throw new Error('Publish button not found')
    await publish.trigger('click')
    expect(wrapper.emitted('publish')?.[0]?.[0]).toMatchObject({
      requiresAuthentication: true,
      runtimeVisibility: 'private',
    })
    expect(wrapper.emitted('publish')?.[0]?.[0]).not.toHaveProperty('category')
    expect(wrapper.emitted('publish')?.[0]?.[0]).not.toHaveProperty('tags')
  })
})
