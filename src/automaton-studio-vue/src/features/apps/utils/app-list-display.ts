import type { AppListItem } from '../types/apps.types'

export type AppFilter = 'all' | 'web' | 'actions' | 'stopped'
export type AppKind = 'Web app' | 'Website' | 'Action'

export type AppStateTone = 'live' | 'ok' | 'stopped' | 'failing'

export interface AppState {
  label: string
  tone: AppStateTone
}

/** The generated-mark palette: background and letter colour. */
export const markPalette = [
  { background: '#fde3d2', color: '#a23d05' },
  { background: '#17181c', color: '#ff7a2e' },
  { background: '#e6e4dd', color: '#17181c' },
  { background: '#f26a1b', color: '#17181c' },
  { background: '#2a2b31', color: '#f1f0eb' },
] as const

/** A stable palette entry for an app id (FNV-1a), so a mark never changes between loads. */
export function markColors(id: string) {
  let hash = 0x811c9dc5
  for (let index = 0; index < id.length; index++) {
    hash ^= id.charCodeAt(index)
    hash = Math.imul(hash, 0x01000193) >>> 0
  }
  return markPalette[hash % markPalette.length] ?? markPalette[0]
}

export function markInitial(name: string): string {
  return (Array.from(name.trim())[0] ?? '?').toUpperCase()
}

export function appKind(app: AppListItem): AppKind {
  if (app.actionsOnly) return 'Action'
  return app.liveEnvironment?.isStaticHosting ? 'Website' : 'Web app'
}


export function appDescription(app: AppListItem): string {
  const text = (app.summary || app.description || '').trim()
  const parts = [appKind(app), text, nextRunLabel(app)].filter(Boolean)
  return parts.join(' · ')
}

/** "next run 02:00" today, otherwise with the date; empty when no schedule is enabled. */
export function nextRunLabel(app: AppListItem, now = new Date()): string {
  if (!app.actionsOnly || !app.nextActionRunAtUtc) return ''
  const next = new Date(app.nextActionRunAtUtc)
  if (Number.isNaN(next.getTime())) return ''
  const time = next.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
  return next.toDateString() === now.toDateString() ? `next run ${time}` : `next run ${next.toLocaleDateString([], { month: 'short', day: 'numeric' })}, ${time}`
}

export function isStopped(app: AppListItem): boolean {
  if (app.isPaused) return true
  if (app.actionsOnly) return false
  const environment = app.liveEnvironment
  if (!environment) return app.runner?.connectivityStatus !== 'Online'
  return !(environment.genericRuntimeDeployed && environment.status === 'Running' && (environment.runnerOnline || environment.isStaticHosting))
}

/** The list data has no schedule, so an action shows when it last ran. */
export function appState(app: AppListItem): AppState {
  if (app.isPaused) return { label: 'paused', tone: 'stopped' }
  if (app.actionsOnly) {
    const run = app.lastActionRun
    if (!run) return { label: 'no runs yet', tone: 'stopped' }
    const ok = run.status === 'Succeeded'
    const failed = ['Failed', 'TimedOut', 'Lost', 'RunnerUnavailable'].includes(run.status)
    return { label: failed ? 'failing' : `${ok ? 'ran' : run.status.toLowerCase()} ${formatShortTime(run.createdAtUtc)}`, tone: ok ? 'ok' : failed ? 'failing' : 'stopped' }
  }
  if (app.liveEnvironment?.status === 'Failed') return { label: 'failing', tone: 'failing' }
  return isStopped(app) ? { label: 'stopped', tone: 'stopped' } : { label: 'live', tone: 'live' }
}

export function formatShortTime(iso: string, now = new Date()): string {
  const minutes = Math.max(0, Math.floor((now.getTime() - new Date(iso).getTime()) / 60000))
  if (minutes < 1) return 'just now'
  if (minutes < 60) return `${minutes}m ago`
  if (minutes < 60 * 24) return `${Math.floor(minutes / 60)}h ago`
  return `${Math.floor(minutes / 1440)}d ago`
}

export function matchesFilter(app: AppListItem, filter: AppFilter): boolean {
  switch (filter) {
    case 'web': return !app.actionsOnly
    case 'actions': return !!app.actionsOnly
    case 'stopped': return isStopped(app)
    default: return true
  }
}

export function matchesSearch(app: AppListItem, query: string): boolean {
  const needle = query.trim().toLowerCase()
  if (!needle) return true
  return [app.name, app.summary, app.description, ...app.tags].some((text) => text?.toLowerCase().includes(needle))
}

/** Official apps are named "Automaton Flow Check"; the list already says it is Automaton's, so drop the prefix. */
export function catalogName(name: string): string {
  const short = name.replace(/^Automaton\s+/i, '').trim()
  return short || name
}
