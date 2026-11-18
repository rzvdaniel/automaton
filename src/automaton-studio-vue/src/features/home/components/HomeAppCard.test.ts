// @vitest-environment jsdom
import { mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import HomeAppCard from './HomeAppCard.vue'
import type { AppHomeCard } from '../types/home.types'

vi.mock('vue-chartjs', () => ({
  Line: { template: '<canvas data-testid="chart-line" />' },
  Bar: { template: '<canvas data-testid="chart-bar" />' },
  Doughnut: { template: '<canvas data-testid="chart-donut" />' },
}))

const item = (card: Partial<AppHomeCard['card']>, stale = false): AppHomeCard => ({
  card: { status: 'Healthy', facts: [], alerts: [], ...card },
  updatedAtUtc: new Date().toISOString(),
  stale,
})
const mountCard = (value: AppHomeCard) => mount(HomeAppCard, { props: { appId: 'app1', item: value } })

describe('HomeAppCard', () => {
  it('keeps the plain status, facts and alerts card working', () => {
    const wrapper = mountCard(item({ facts: [{ label: 'Disk', value: '61%' }], alerts: [{ severity: 'warning', text: 'Late' }] }))
    expect(wrapper.get('[data-testid="app-card-status"]').text()).toBe('Healthy')
    expect(wrapper.text()).toContain('Disk')
    expect(wrapper.get('[data-testid="app-card-alert"]').text()).toContain('Late')
  })

  it('draws a meter with threshold marks in the tone of its value', () => {
    const wrapper = mountCard(item({ tone: 'warning', blocks: [{ type: 'meter', label: 'Disk', value: 80, max: 100, unit: '%', warnAt: 75, critAt: 90 }] }))
    expect(wrapper.classes()).toContain('hc--warning')
    const meter = wrapper.get('[data-testid="card-meter"]')
    expect(meter.classes()).toContain('hc-tone--warning')
    expect(meter.findAll('.hc-meter__mark')).toHaveLength(2)
    expect(meter.get('.hc-meter__fill').attributes('style')).toContain('width: 80%')
  })

  it('shows counters, badge, list, own-page buttons and a chart; text is never HTML', () => {
    const wrapper = mountCard(item({
      detail: '<b>x</b>',
      blocks: [
        { type: 'counters', items: [{ label: 'Up', value: '12', tone: 'ok' }, { label: 'Down', value: '1', tone: 'critical' }] },
        { type: 'badge', label: 'State', text: 'Up', tone: 'ok' },
        { type: 'list', rows: [{ text: 'a.example' }] },
        { type: 'buttons', items: [{ label: 'Open', path: '/status' }] },
        { type: 'chart', kind: 'line', labels: ['a', 'b'], series: [{ name: 'ms', tone: 'info', values: [1, 2] }] },
      ],
    }))
    expect(wrapper.findAll('.hc-counter')).toHaveLength(2)
    expect(wrapper.find('[data-testid="card-badge"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="card-list"]').text()).toContain('a.example')
    expect(wrapper.get('[data-testid="card-buttons"] a').attributes('rel')).toContain('noopener')
    expect(wrapper.find('[data-testid="chart-line"]').exists()).toBe(true)
    expect(wrapper.find('.hc__detail').html()).not.toContain('<b>')
  })

  it('greys a stale card', () => {
    expect(mountCard(item({}, true)).classes()).toContain('hc--stale')
  })
})
