import type { AppListItem } from '@/features/apps/types/apps.types'
import { formatShortTime } from '@/features/apps/utils/app-list-display'

/** The platform facts shown on a pinned app's card, from the data every app list item already has. */
export function appFacts(app: AppListItem, now = new Date()): { label: string; value: string }[] {
  const facts: { label: string; value: string }[] = []
  const deployed = app.liveEnvironment?.lastSuccessfulDeploymentAtUtc
  facts.push({ label: 'Last deploy', value: deployed ? formatShortTime(deployed, now) : 'never' })
  const run = app.lastActionRun
  if (run) facts.push({ label: 'Last Action run', value: `${run.moduleKey} ${run.status.toLowerCase()} ${formatShortTime(run.createdAtUtc, now)}` })
  facts.push({ label: 'Runner', value: app.runner?.name ?? 'none' })
  return facts
}

/** Moves one item to a new index, for drag and drop. */
export function moved<T>(items: T[], from: number, to: number): T[] {
  if (from === to || from < 0 || to < 0 || from >= items.length || to >= items.length) return items
  const next = items.slice()
  const [item] = next.splice(from, 1)
  next.splice(to, 0, item as T)
  return next
}
