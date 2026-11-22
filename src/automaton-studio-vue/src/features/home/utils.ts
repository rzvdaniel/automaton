import type { AppListItem } from '@/features/apps/types/apps.types'
import { formatShortTime } from '@/features/apps/utils/app-list-display'

/** The one muted footer line of a pinned card: when it was deployed (or last ran) and when its card was updated. */
export function footerLine(app: AppListItem, card: { updatedAtUtc: string; stale: boolean } | null | undefined, now = new Date()): string {
  const parts: string[] = []
  const deployed = app.liveEnvironment?.lastSuccessfulDeploymentAtUtc
  if (deployed) parts.push(`Deployed ${formatShortTime(deployed, now)}`)
  else if (app.lastActionRun) parts.push(`Last run ${formatShortTime(app.lastActionRun.createdAtUtc, now)}`)
  else parts.push('Not deployed yet')
  if (card) parts.push(card.stale ? `Not up to date, updated ${formatShortTime(card.updatedAtUtc, now)}` : `updated ${formatShortTime(card.updatedAtUtc, now)}`)
  return parts.join(' \u00b7 ')
}

export interface SlotRect { left: number; top: number; width: number; height: number }

/** The index of the rectangle the point is inside, ignoring `skip`; -1 when it is over none (or only over the gaps). */
export function slotAt(rects: SlotRect[], x: number, y: number, skip: number): number {
  return rects.findIndex((r, i) => i !== skip && x >= r.left && x <= r.left + r.width && y >= r.top && y <= r.top + r.height)
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
