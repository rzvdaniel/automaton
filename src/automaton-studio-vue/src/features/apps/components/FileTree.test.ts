// @vitest-environment jsdom
import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import PrimeVue from 'primevue/config'
import FileTree from './FileTree.vue'

describe('FileTree', () => {
  it('renders stored files without source mutation controls', () => {
    const wrapper = mount(FileTree, {
      props: {
        title: 'Stored files',
        nodes: [{ key: 'source', name: 'source', path: 'source', type: 'folder', children: [] }],
      },
      global: { plugins: [PrimeVue] },
    })

    expect(wrapper.text()).toContain('Stored files')
    expect(wrapper.text()).not.toContain('Create File')
    expect(wrapper.text()).not.toContain('Delete')
    expect(wrapper.find('[aria-label="Create File"]').exists()).toBe(false)
    expect(wrapper.find('[aria-label="Delete"]').exists()).toBe(false)
  })
})
