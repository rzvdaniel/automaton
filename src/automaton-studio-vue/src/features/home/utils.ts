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

export interface MosaicPlace { column: number; span: number; top: number }

/**
 * Masonry placement: each card goes into the shortest column; a wide card takes the two neighbouring columns
 * whose taller one is lowest. Order is kept, so the first card is always top left. One column ignores wide.
 */
export function mosaic(heights: number[], wide: boolean[], columns: number, gap: number): { places: MosaicPlace[]; height: number } {
  const count = Math.max(1, columns)
  const tops = new Array<number>(count).fill(0)
  const places = heights.map((height, index) => {
    const span = wide[index] && count > 1 ? 2 : 1
    let best = 0
    let bestTop = Infinity
    for (let column = 0; column + span <= count; column++) {
      const top = Math.max(...tops.slice(column, column + span))
      if (top < bestTop) { best = column; bestTop = top }
    }
    for (let column = best; column < best + span; column++) tops[column] = bestTop + height + gap
    return { column: best, span, top: bestTop }
  })
  return { places, height: Math.max(0, Math.max(...tops) - gap) }
}
