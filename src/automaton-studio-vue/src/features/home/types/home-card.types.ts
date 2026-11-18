export type CardTone = 'ok' | 'warning' | 'critical' | 'info'

export interface ChartSeries { name: string; tone: CardTone; values: number[] }

/** A validated Home card block; the platform keeps only known blocks with known fields. */
export type CardBlock =
  | { type: 'fact'; label: string; value: string; note?: string }
  | { type: 'badge'; label: string; text: string; tone: CardTone; note?: string }
  | { type: 'meter'; label: string; value: number; max: number; unit?: string; warnAt?: number; critAt?: number; note?: string }
  | { type: 'counters'; items: { label: string; value: string; tone?: CardTone }[] }
  | { type: 'list'; rows: { text: string; tone?: CardTone; note?: string }[] }
  | { type: 'buttons'; items: { label: string; path: string }[] }
  | { type: 'chart'; kind: 'line' | 'area' | 'bar' | 'stacked-bar' | 'donut' | 'gauge'; title?: string; labels: string[]; series: ChartSeries[]; max?: number }
