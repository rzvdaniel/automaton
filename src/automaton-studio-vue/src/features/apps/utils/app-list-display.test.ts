import { describe, expect, it } from 'vitest'
import type { AppListItem } from '../types/apps.types'
import { appDescription, appKind, appState, formatShortTime, nextRunLabel, markColors, markInitial, markPalette, matchesFilter, matchesSearch } from './app-list-display'

const app = (over: Partial<AppListItem> = {}): AppListItem => ({
  id: 'a1', name: 'Notes', summary: '', description: '', tags: [], isPublished: false, updatedAtUtc: '2026-01-01T00:00:00Z',
  canDelete: true, runtime: { genericRuntimeDeployed: true },
  liveEnvironment: { name: 'Live', status: 'Running', isHostedRunner: false, runnerOnline: true, genericRuntimeDeployed: true, routePath: '/' },
  ...over,
})

describe('app list display', () => {
  it('picks the same mark colours for an id every time, from the palette', () => {
    const id = '3f2b1c9e-0a41-4c55-9f0e-5d6f7a8b9c10'
    expect(markColors(id)).toEqual(markColors(id))
    expect(markPalette).toContainEqual(markColors(id))
    const used = new Set(Array.from({ length: 60 }, (_, i) => markColors(`app-${i}`).background))
    expect(used.size).toBeGreaterThan(2)
  })

  it('uses the first letter of the name, capitalised', () => {
    expect(markInitial('  notes')).toBe('N')
    expect(markInitial('')).toBe('?')
  })

  it('tells web apps, websites and actions apart', () => {
    expect(appKind(app())).toBe('Web app')
    expect(appKind(app({ liveEnvironment: { ...app().liveEnvironment!, isStaticHosting: true } }))).toBe('Website')
    expect(appKind(app({ actionsOnly: true }))).toBe('Action')
    expect(appDescription(app({ summary: 'Short links' }))).toBe('Web app · Short links')
    expect(appDescription(app())).toBe('Web app')
  })

  it('shows the next scheduled run of an Actions-only app in its description', () => {
    const now = new Date()
    const soon = new Date(now.getTime() + 60_000).toISOString()
    const time = new Date(soon).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
    expect(appDescription(app({ actionsOnly: true, summary: 'Backs up', nextActionRunAtUtc: soon }))).toContain(`Backs up · next run ${time}`)
    expect(appDescription(app({ actionsOnly: true, nextActionRunAtUtc: null }))).toBe('Action')
    expect(appDescription(app({ nextActionRunAtUtc: soon }))).toBe('Web app')
    const later = new Date(now.getTime() + 3 * 86400_000).toISOString()
    expect(nextRunLabel(app({ actionsOnly: true, nextActionRunAtUtc: later }), now)).toMatch(/^next run .+, /)
  })

  it('derives state: live, stopped, and the last run for actions', () => {
    expect(appState(app()).label).toBe('live')
    expect(appState(app({ liveEnvironment: { ...app().liveEnvironment!, runnerOnline: false } })).tone).toBe('stopped')
    expect(appState(app({ liveEnvironment: null, runner: null })).label).toBe('stopped')
    expect(appState(app({ actionsOnly: true })).label).toBe('no runs yet')
    const run = { moduleKey: 'm', status: 'Succeeded', createdAtUtc: new Date(Date.now() - 3 * 3600_000).toISOString() }
    expect(appState(app({ actionsOnly: true, lastActionRun: run }))).toEqual({ label: 'ran 3h ago', tone: 'ok' })
    expect(appState(app({ actionsOnly: true, lastActionRun: { ...run, status: 'Failed' } }))).toEqual({ label: 'failing', tone: 'failing' })
    expect(appState(app({ liveEnvironment: { ...app().liveEnvironment!, status: 'Failed' } }))).toEqual({ label: 'failing', tone: 'failing' })
  })

  it('filters by kind and stopped state', () => {
    const web = app({ id: 'w' })
    const action = app({ id: 'x', actionsOnly: true })
    const stopped = app({ id: 's', liveEnvironment: null, runner: null })
    const all = [web, action, stopped]
    expect(all.filter((a) => matchesFilter(a, 'all'))).toHaveLength(3)
    expect(all.filter((a) => matchesFilter(a, 'web')).map((a) => a.id)).toEqual(['w', 's'])
    expect(all.filter((a) => matchesFilter(a, 'actions')).map((a) => a.id)).toEqual(['x'])
    expect(all.filter((a) => matchesFilter(a, 'stopped')).map((a) => a.id)).toEqual(['s'])
  })

  it('searches name, description and tags', () => {
    expect(matchesSearch(app({ name: 'Site Links' }), 'links')).toBe(true)
    expect(matchesSearch(app({ summary: 'Track invoices' }), 'INVOICE')).toBe(true)
    expect(matchesSearch(app({ tags: ['crm'] }), 'crm')).toBe(true)
    expect(matchesSearch(app(), 'zzz')).toBe(false)
    expect(matchesSearch(app(), '  ')).toBe(true)
  })

  it('formats elapsed time', () => {
    const now = new Date('2026-01-02T00:00:00Z')
    expect(formatShortTime('2026-01-01T23:59:40Z', now)).toBe('just now')
    expect(formatShortTime('2026-01-01T23:30:00Z', now)).toBe('30m ago')
    expect(formatShortTime('2025-12-30T00:00:00Z', now)).toBe('3d ago')
  })
})
