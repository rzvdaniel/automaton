// @vitest-environment jsdom
import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'

vi.mock('primevue/usetoast', () => ({ useToast: () => ({ add: vi.fn() }) }))
vi.mock('../services/apps.service', () => ({
  appsService: {
    getSourceFileTree: vi.fn().mockResolvedValue([{ name: 'index.html', path: 'index.html', type: 'file' }]),
    readSourceFile: vi.fn().mockResolvedValue('{"stored": true}'),
  },
}))

import AppSourceTab from './AppSourceTab.vue'

const stubs = {
  Select: true,
  Splitter: { template: '<div><slot /></div>' },
  SplitterPanel: { template: '<div><slot /></div>' },
  FileTree: { props: ['nodes'], template: '<div data-testid="tree">{{ nodes.length }}</div>' },
  CodeEditor: { props: ['filePath', 'content'], template: '<pre data-testid="code">{{ filePath }}|{{ content }}</pre>' },
}

describe('AppSourceTab', () => {
  it('opens on the pinned definition, then lists the stored files', async () => {
    const wrapper = mount(AppSourceTab, { props: { appId: 'a1', definition: { name: 'Demo' } }, global: { stubs } })
    await flushPromises()
    expect(wrapper.get('[data-testid="source-definition"]').text()).toContain('Definition (automaton.json)')
    expect(wrapper.get('[data-testid="code"]').text()).toContain('automaton.json|')
    expect(wrapper.get('[data-testid="code"]').text()).toContain('"stored": true')
    expect(wrapper.get('[data-testid="tree"]').text()).toBe('1')
  })
})
