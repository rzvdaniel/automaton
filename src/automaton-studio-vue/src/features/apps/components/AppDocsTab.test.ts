// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { appsService } from '../services/apps.service'
import AppDocsTab from './AppDocsTab.vue'

vi.mock('../services/apps.service', () => ({
  appsService: {
    readSourceFile: vi.fn(),
    readSourceImage: vi.fn(),
  },
}))

describe('AppDocsTab', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    vi.mocked(appsService.readSourceFile).mockResolvedValue('# Agent Keeper\n\n## Install and set up\n\nSteps.\n\n## Settings\n\nDetails.')
  })

  it('shows At a glance above the README with Pages, Scheduled and Needs, and lists it first in On this page', async () => {
    const wrapper = mount(AppDocsTab, {
      props: {
        appId: 'app-1',
        version: 'b166c9f',
        application: {
          pages: [{ path: '/', label: 'Agent queue' }],
          schedules: [{ name: 'Pick up', action: 'pick-up', cron: '*/10 * * * *' }],
          secrets: [{ name: 'AGENT_GH_TOKEN' }],
          permissions: ['runner:host-exec'],
        } as never,
      },
    })
    await flushPromises()

    const glance = wrapper.get('[data-testid="docs-at-a-glance"]')
    expect(glance.get('[data-testid="glance-pages"]').text()).toContain('Agent queue')
    expect(glance.get('[data-testid="glance-scheduled"]').text()).toContain('Pick up · every 10 minutes')
    expect(glance.get('[data-testid="glance-needs"]').text()).toContain('Secret AGENT_GH_TOKEN · host access')

    const outlineItems = wrapper.findAll('[data-testid="docs-outline"] button').map((button) => button.text())
    expect(outlineItems).toEqual(['At a glance', 'Agent Keeper', 'Install and set up', 'Settings'])
  })

  it('omits empty At a glance rows and hides the card when nothing is declared', async () => {
    const partial = mount(AppDocsTab, {
      props: {
        appId: 'app-1',
        application: {
          pages: [],
          schedules: [],
          secrets: [{ name: 'API_KEY' }],
          permissions: [],
        } as never,
      },
    })
    await flushPromises()
    expect(partial.find('[data-testid="glance-pages"]').exists()).toBe(false)
    expect(partial.find('[data-testid="glance-scheduled"]').exists()).toBe(false)
    expect(partial.get('[data-testid="glance-needs"]').text()).toContain('Secret API_KEY')

    const empty = mount(AppDocsTab, {
      props: {
        appId: 'app-1',
        application: { pages: [], schedules: [], secrets: [], permissions: [] } as never,
      },
    })
    await flushPromises()
    expect(empty.find('[data-testid="docs-at-a-glance"]').exists()).toBe(false)
    const outlineItems = empty.findAll('[data-testid="docs-outline"] button').map((button) => button.text())
    expect(outlineItems[0]).toBe('Agent Keeper')
  })
})
