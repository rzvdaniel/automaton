<script setup lang="ts">
import { computed } from 'vue'
import { Bar, Doughnut, Line } from 'vue-chartjs'
import {
  ArcElement, BarElement, CategoryScale, Chart as ChartJS, Filler, LinearScale, LineElement, PointElement, Tooltip,
  type ChartOptions,
} from 'chart.js'
import type { CardBlock, CardTone } from '../types/home-card.types'

ChartJS.register(ArcElement, BarElement, CategoryScale, Filler, LinearScale, LineElement, PointElement, Tooltip)

type ChartBlock = Extract<CardBlock, { type: 'chart' }>
const props = defineProps<{ block: ChartBlock; colors: Record<CardTone | 'ink' | 'line' | 'soft', string> }>()

const tone = (value: CardTone) => props.colors[value]
const shade = (color: string, percent: number) => `color-mix(in srgb, ${color} ${percent}%, transparent)`
const filled = computed(() => props.block.kind === 'area')
const labels = computed(() => props.block.labels)

const datasets = computed(() => props.block.series.map((series) => ({
  label: series.name,
  data: series.values,
  borderColor: tone(series.tone),
  backgroundColor: props.block.kind === 'line' || filled.value ? shade(tone(series.tone), filled.value ? 14 : 0) : tone(series.tone),
  fill: filled.value,
  tension: 0.3,
  pointRadius: props.block.kind === 'line' || filled.value ? series.values.map((_, i) => (i === series.values.length - 1 ? 3.5 : 0)) : 0,
  pointBackgroundColor: tone(series.tone),
  borderWidth: 2,
  borderRadius: 3,
})))

const axisOptions = computed<ChartOptions<'line' | 'bar'>>(() => {
  const stacked = props.block.kind === 'stacked-bar'
  return {
    responsive: true,
    maintainAspectRatio: false,
    animation: false,
    interaction: { mode: 'index', intersect: false },
    plugins: { legend: { display: false } },
    scales: {
      x: { stacked, grid: { display: false }, ticks: { color: props.colors.soft, maxTicksLimit: 6, font: { size: 10 } }, border: { color: props.colors.line } },
      y: { stacked, grid: { color: props.colors.line }, ticks: { color: props.colors.soft, maxTicksLimit: 4, font: { size: 10 } }, border: { display: false } },
    },
  }
})

const round = computed<ChartOptions<'doughnut'>>(() => ({
  responsive: true,
  maintainAspectRatio: false,
  animation: false,
  cutout: '72%',
  plugins: { legend: { display: false } },
  ...(props.block.kind === 'gauge' ? { rotation: -90, circumference: 180 } : {}),
}))

const donut = computed(() => {
  const series = props.block.series[0]
  const palette: CardTone[] = ['info', 'ok', 'warning', 'critical']
  return {
    labels: labels.value,
    datasets: [{
      data: series?.values ?? [],
      backgroundColor: (series?.values ?? []).map((_, index) => tone(palette[index % palette.length] as CardTone)),
      borderColor: props.colors.line,
      borderWidth: 1,
    }],
  }
})

const gauge = computed(() => {
  const series = props.block.series[0]
  const value = Math.min(Math.max(series?.values[0] ?? 0, 0), props.block.max ?? 100)
  return {
    datasets: [{ data: [value, (props.block.max ?? 100) - value], backgroundColor: [tone(series?.tone ?? 'info'), props.colors.line], borderWidth: 0 }],
  }
})

const gaugeText = computed(() => String(props.block.series[0]?.values[0] ?? ''))
const summary = computed(() => `${props.block.title ?? 'Chart'}: ${props.block.series.map((series) => `${series.name} ${series.values.join(', ')}`).join('; ')}`)
</script>

<template>
  <figure class="hc-chart" data-testid="card-chart" :data-kind="block.kind">
    <figcaption v-if="block.title" class="hc-chart__title">{{ block.title }}</figcaption>
    <div class="hc-chart__canvas" :class="{ 'hc-chart__canvas--round': block.kind === 'donut' || block.kind === 'gauge' }" role="img" :aria-label="summary">
      <Line v-if="block.kind === 'line' || block.kind === 'area'" :data="{ labels, datasets }" :options="axisOptions as ChartOptions<'line'>" />
      <Bar v-else-if="block.kind === 'bar' || block.kind === 'stacked-bar'" :data="{ labels, datasets }" :options="axisOptions as ChartOptions<'bar'>" />
      <Doughnut v-else-if="block.kind === 'donut'" :data="donut" :options="round" />
      <template v-else>
        <Doughnut :data="gauge" :options="round" />
        <span class="hc-chart__gauge">{{ gaugeText }}</span>
      </template>
    </div>
  </figure>
</template>

<style scoped>
.hc-chart { margin: 0; }
.hc-chart__title { font-family: var(--mono); font-size: 10.5px; letter-spacing: 0.06em; text-transform: uppercase; color: var(--ink-soft); margin-bottom: 4px; }
.hc-chart__canvas { position: relative; height: 96px; }
.hc-chart__canvas--round { height: 110px; }
.hc-chart__gauge { position: absolute; left: 0; right: 0; bottom: 4px; text-align: center; font-family: var(--display); font-weight: 700; font-size: 22px; color: var(--ink); }
</style>
