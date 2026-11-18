<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { formatShortTime } from '@/features/apps/utils/app-list-display'
import { getLaunchTargetUrl } from '@/features/apps/utils/public-app-url'
import type { AppHomeCard } from '../types/home.types'
import type { CardBlock, CardTone } from '../types/home-card.types'
import HomeCardChart from './HomeCardChart.vue'

const props = defineProps<{ appId: string; item: AppHomeCard }>()

const root = ref<HTMLElement | null>(null)
const colors = ref({ ok: '#1f6b4f', warning: '#f26a1b', critical: '#c0392b', info: '#55565e', ink: '#17181c', line: '#e2e0da', soft: '#55565e' })
/** Chart.js needs real colour values, so read the themed tokens again whenever light/dark changes. */
let observer: MutationObserver | null = null
function readColors() {
  if (!root.value) return
  const style = getComputedStyle(root.value)
  const read = (name: string, fallback: string) => style.getPropertyValue(name).trim() || fallback
  colors.value = {
    ok: read('--hc-ok', colors.value.ok), warning: read('--hc-warning', colors.value.warning), critical: read('--hc-critical', colors.value.critical),
    info: read('--hc-info', colors.value.info), ink: read('--ink', colors.value.ink), line: read('--line', colors.value.line), soft: read('--ink-soft', colors.value.soft),
  }
}
onMounted(() => {
  readColors()
  observer = new MutationObserver(readColors)
  observer.observe(document.documentElement, { attributes: true, attributeFilter: ['class'] })
})
onBeforeUnmount(() => observer?.disconnect())

const card = computed(() => props.item.card)
const toneOf = (value?: CardTone | null) => value ?? 'info'
const blocks = computed<CardBlock[]>(() => card.value.blocks ?? [])
/** A card with nothing to draw but a reason (for example a missing setting): one line and a link to the fix. */
const noData = computed(() => !!card.value.detail && blocks.value.length === 0 && card.value.facts.length === 0 && card.value.alerts.length === 0)

/** Meter state from its thresholds: critical at critAt, warning at warnAt, else ok. */
function meterTone(block: Extract<CardBlock, { type: 'meter' }>): CardTone {
  if (block.critAt !== undefined && block.value >= block.critAt) return 'critical'
  if (block.warnAt !== undefined && block.value >= block.warnAt) return 'warning'
  return 'ok'
}
const percent = (value: number, max: number) => `${Math.min(100, Math.max(0, (value / max) * 100))}%`
const number = (value: number) => new Intl.NumberFormat().format(value)
const link = (path: string) => getLaunchTargetUrl(props.appId, path)
</script>

<template>
  <div
    ref="root"
    class="hc"
    :class="[`hc--${card.tone ?? 'none'}`, { 'hc--stale': item.stale }]"
    data-testid="app-card"
  >
    <p v-if="noData" class="hc__nodata" data-testid="app-card-nodata">
      <span class="hc__nodata-text"><strong>{{ card.status }}.</strong> {{ card.detail }}</span>
      <RouterLink :to="{ path: `/apps/${appId}`, query: { view: 'settings' } }" class="hc__nodata-link" data-testid="app-card-setup">Set up</RouterLink>
    </p>
    <template v-else>
      <p class="hc__status" data-testid="app-card-status">{{ card.status }}</p>
      <p v-if="card.detail" class="hc__detail">{{ card.detail }}</p>
    </template>

    <template v-for="(block, index) in blocks" :key="index">
      <div v-if="block.type === 'fact'" class="hc-fact" data-testid="card-fact">
        <span class="hc-fact__label">{{ block.label }}</span>
        <span class="hc-fact__value">{{ block.value }}</span>
        <span v-if="block.note" class="hc-note">{{ block.note }}</span>
      </div>

      <div v-else-if="block.type === 'badge'" class="hc-fact" data-testid="card-badge">
        <span class="hc-fact__label">{{ block.label }}</span>
        <span class="hc-badge" :class="`hc-tone--${block.tone}`">{{ block.text }}</span>
        <span v-if="block.note" class="hc-note">{{ block.note }}</span>
      </div>

      <div v-else-if="block.type === 'meter'" class="hc-meter" :class="`hc-tone--${meterTone(block)}`" data-testid="card-meter">
        <div class="hc-meter__head">
          <span class="hc-fact__label">{{ block.label }}</span>
          <span class="hc-meter__value">{{ number(block.value) }}<small>{{ block.unit ?? '' }}</small></span>
        </div>
        <div class="hc-meter__bar" role="meter" :aria-label="block.label" :aria-valuenow="block.value" aria-valuemin="0" :aria-valuemax="block.max">
          <div class="hc-meter__fill" :style="{ width: percent(block.value, block.max) }" />
          <span v-if="block.warnAt !== undefined" class="hc-meter__mark" :style="{ left: percent(block.warnAt, block.max) }" />
          <span v-if="block.critAt !== undefined" class="hc-meter__mark hc-meter__mark--crit" :style="{ left: percent(block.critAt, block.max) }" />
        </div>
        <span v-if="block.note" class="hc-note">{{ block.note }}</span>
      </div>

      <div v-else-if="block.type === 'counters'" class="hc-counters" data-testid="card-counters">
        <div v-for="counter in block.items" :key="counter.label" class="hc-counter" :class="`hc-tone--${toneOf(counter.tone)}`">
          <span class="hc-counter__value">{{ counter.value }}</span>
          <span class="hc-fact__label">{{ counter.label }}</span>
        </div>
      </div>

      <ul v-else-if="block.type === 'list'" class="hc-list" data-testid="card-list">
        <li v-for="(row, rowIndex) in block.rows" :key="rowIndex" :class="`hc-tone--${toneOf(row.tone)}`">
          <span class="hc-dot" />
          <span class="hc-list__text">{{ row.text }}</span>
          <span v-if="row.note" class="hc-note">{{ row.note }}</span>
        </li>
      </ul>

      <div v-else-if="block.type === 'buttons'" class="hc-buttons" data-testid="card-buttons">
        <a v-for="button in block.items" :key="button.path" :href="link(button.path)" target="_blank" rel="noopener noreferrer" class="hc-button">{{ button.label }}</a>
      </div>

      <HomeCardChart v-else-if="block.type === 'chart'" :block="block" :colors="colors" />
    </template>

    <dl v-if="card.facts.length > 0" class="hc-facts">
      <div v-for="fact in card.facts" :key="fact.label">
        <dt>{{ fact.label }}</dt>
        <dd>{{ fact.value }}</dd>
      </div>
    </dl>
    <ul v-if="card.alerts.length > 0" class="hc-alerts">
      <li v-for="(alert, alertIndex) in card.alerts" :key="alertIndex" :class="`hc-alert--${alert.severity}`" data-testid="app-card-alert">
        <span class="hc-alert__level">{{ alert.severity }}</span> {{ alert.text }}
      </li>
    </ul>
    <p class="hc__time" data-testid="app-card-time">
      {{ item.stale ? 'Not up to date. Last update ' : 'Updated ' }}{{ formatShortTime(item.updatedAtUtc) }}
    </p>
  </div>
</template>

<style scoped>
.hc {
  --hc-ok: var(--ok); --hc-warning: var(--signal); --hc-critical: #c0392b; --hc-info: var(--ink-soft);
  position: relative; display: flex; flex-direction: column; gap: 10px; padding: 12px 12px 10px; border-radius: 10px;
  background: var(--row-line); overflow: hidden; container-type: inline-size;
}
:global(.app-dark) .hc { --hc-critical: #ff6b5b; }
.hc::before { content: ''; position: absolute; inset: 0 0 auto 0; height: 3px; background: var(--hc-edge, transparent); }
.hc--ok { --hc-edge: var(--hc-ok); }
.hc--warning { --hc-edge: var(--hc-warning); }
.hc--critical { --hc-edge: var(--hc-critical); }
.hc--info { --hc-edge: var(--hc-info); }
.hc--stale { opacity: 0.55; filter: grayscale(1); }
.hc__status { margin: 0; font-family: var(--display); font-weight: 700; font-size: 17px; line-height: 1.2; overflow-wrap: anywhere; color: var(--ink); }
.hc__detail { margin: -6px 0 0; font-size: 13px; color: var(--ink-soft); overflow-wrap: anywhere; }
.hc__nodata { margin: 0; display: flex; flex-wrap: wrap; align-items: baseline; gap: 2px 10px; font-size: 13px; color: var(--ink-soft); }
.hc__nodata-text { flex: 1 1 14ch; min-width: 0; overflow-wrap: anywhere; }
.hc__nodata-text strong { color: var(--ink); }
.hc__nodata-link { font-weight: 600; color: var(--ink); }
.hc__nodata-link:focus-visible { outline: 2px solid var(--signal); outline-offset: 2px; }
.hc__time { margin: 0; font-size: 12px; color: var(--ink-soft); }
.hc-tone--ok { --hc-tone: var(--hc-ok); }
.hc-tone--warning { --hc-tone: var(--hc-warning); }
.hc-tone--critical { --hc-tone: var(--hc-critical); }
.hc-tone--info { --hc-tone: var(--hc-info); }
.hc-note { font-size: 12px; color: var(--ink-soft); }
.hc-fact { display: flex; flex-wrap: wrap; align-items: baseline; justify-content: space-between; gap: 2px 8px; font-size: 13px; }
.hc-fact__label { font-family: var(--mono); font-size: 10.5px; letter-spacing: 0.06em; text-transform: uppercase; color: var(--ink-soft); }
.hc-fact__value { font-weight: 600; overflow-wrap: anywhere; }
.hc-badge { padding: 2px 9px; border-radius: 999px; font-family: var(--mono); font-size: 11.5px; font-weight: 600; color: var(--hc-tone); border: 1px solid var(--hc-tone); }
.hc-meter { display: flex; flex-direction: column; gap: 5px; }
.hc-meter__head { display: flex; justify-content: space-between; align-items: baseline; gap: 8px; }
.hc-meter__value { font-family: var(--display); font-weight: 700; font-size: 20px; color: var(--ink); }
.hc-meter__value small { font-size: 12px; font-weight: 500; color: var(--ink-soft); margin-left: 2px; }
.hc-meter__bar { position: relative; height: 12px; border-radius: 6px; background: var(--line); }
.hc-meter__fill { height: 100%; border-radius: 6px; background: var(--hc-tone); }
.hc-meter__mark { position: absolute; top: -2px; bottom: -2px; width: 2px; margin-left: -1px; background: var(--ink-soft); opacity: 0.8; }
.hc-meter__mark--crit { background: var(--hc-critical); opacity: 1; }
.hc-counters { display: grid; grid-template-columns: repeat(auto-fit, minmax(64px, 1fr)); gap: 8px; }
.hc-counter { display: flex; flex-direction: column; gap: 2px; min-width: 0; }
.hc-counter__value { font-family: var(--display); font-weight: 700; font-size: 30px; line-height: 1; color: var(--hc-tone, var(--ink)); }
.hc-list { margin: 0; padding: 0; list-style: none; display: grid; gap: 4px; font-size: 13px; }
.hc-list li { display: flex; align-items: baseline; gap: 8px; }
.hc-list__text { flex: 1; min-width: 0; overflow-wrap: anywhere; }
.hc-dot { flex: none; width: 7px; height: 7px; border-radius: 50%; background: var(--hc-tone, var(--ink-soft)); }
.hc-buttons { display: flex; flex-wrap: wrap; gap: 6px; }
.hc-button { padding: 4px 12px; border: 1px solid var(--line); border-radius: 999px; font-size: 12.5px; font-weight: 600; color: var(--ink); text-decoration: none; background: var(--paper); }
.hc-button:hover { border-color: var(--signal); }
.hc-button:focus-visible { outline: 2px solid var(--signal); outline-offset: 2px; }
.hc-facts { margin: 0; display: grid; gap: 4px; font-size: 13px; }
.hc-facts div { display: flex; justify-content: space-between; gap: 8px; }
.hc-facts dt { color: var(--ink-soft); }
.hc-facts dd { margin: 0; text-align: right; overflow-wrap: anywhere; }
.hc-alerts { margin: 0; padding: 0; list-style: none; display: grid; gap: 3px; font-size: 13px; overflow-wrap: anywhere; }
.hc-alert__level { font-family: var(--mono); font-size: 11px; text-transform: uppercase; }
.hc-alert--critical { color: var(--hc-critical); font-weight: 600; }
.hc-alert--warning { color: var(--ink); }
.hc-alert--info { color: var(--ink-soft); }
</style>
