// @vitest-environment jsdom
import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import AppMadeOf from './AppMadeOf.vue'
import type { App } from '../types/apps.types'

const app = {
  developmentMode: 'studio',
  runtimeSource: 'studio',
  application: {
    isStaticSite: true,
    runtime: { command: null, port: 0 },
    storage: { dataDirectory: '', mounts: [] },
    secrets: [],
  },
} as unknown as App

describe('AppMadeOf', () => {
  it('shows the five facts and sends a click to the tab that holds it', async () => {
    const wrapper = mount(AppMadeOf, { props: { app } })
    expect(wrapper.text()).toContain('What this app is made of')
    expect(wrapper.find('[data-testid="made-of-runs"]').text()).toContain('Static site')
    await wrapper.find('[data-testid="made-of-secrets"] button').trigger('click')
    expect(wrapper.emitted('select')?.[0]).toEqual(['settings'])
  })
})
