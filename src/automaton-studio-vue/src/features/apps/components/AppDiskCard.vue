<script setup lang="ts">
import { computed } from 'vue'
import type { AppDiskUsage } from '../types/apps.types'
import { diskParts, formatBytes, hostDiskUse, HOST_DISK_WARNING } from '../utils/app-disk'

const props = defineProps<{ disk: AppDiskUsage; dataDirectory?: string }>()
const parts = computed(() => diskParts(props.disk))
const host = computed(() => hostDiskUse(props.disk))
</script>

<template>
  <section class="card" aria-labelledby="disk-title" data-testid="disk-use">
    <div class="head">
      <h2 id="disk-title">Disk</h2>
      <span v-if="dataDirectory" class="hint">data folder <span class="mono">{{ dataDirectory }}</span></span>
    </div>
    <div class="block">
      <div class="line"><span class="name">This app</span><span class="big" data-testid="disk-total">{{ formatBytes(disk.totalBytes) }}</span></div>
      <div v-if="parts.length" class="bar" role="img" :aria-label="parts.map((part) => `${part.name} ${formatBytes(part.bytes)}`).join(', ')">
        <span v-for="(part, index) in parts" :key="part.name" class="seg" :class="`seg--${index}`" :style="{ width: `${(part.bytes / disk.totalBytes) * 100}%` }"></span>
      </div>
      <p v-if="parts.length" class="hint">{{ parts.map((part) => `${part.name} ${formatBytes(part.bytes)}`).join(' · ') }}</p>
    </div>
    <template v-if="host">
      <div class="rule"></div>
      <div class="block" data-testid="host-disk">
        <div class="line"><span class="name">The disk it lives on</span><span class="big">{{ host.percent }}%</span></div>
        <div class="track" role="img" :aria-label="`${host.percent}% used, warning at ${HOST_DISK_WARNING}%`">
          <span class="fill" :class="{ warn: host.percent >= HOST_DISK_WARNING }" :style="{ width: `${host.percent}%` }"></span>
          <span class="mark" :style="{ left: `${HOST_DISK_WARNING}%` }"></span>
        </div>
        <p class="hint">{{ formatBytes(host.used) }} of {{ formatBytes(host.total) }} · {{ formatBytes(host.free) }} free · warning at {{ HOST_DISK_WARNING }}%</p>
      </div>
    </template>
  </section>
</template>

<style scoped>
.card { display: flex; flex-direction: column; gap: 14px; padding: 20px; border: 1px solid var(--line); border-radius: var(--radius-card); background: var(--paper); }
.head, .line { display: flex; flex-wrap: wrap; align-items: baseline; justify-content: space-between; gap: 8px; }
h2 { margin: 0; font-size: 16px; font-weight: 600; }
.block { display: flex; flex-direction: column; gap: 8px; }
.name { font-weight: 600; }
.big { font-size: 22px; font-weight: 700; }
.hint { margin: 0; color: var(--ink-soft); font-size: 13px; }
.mono { font-family: var(--mono); font-size: 12px; }
.rule { border-top: 1px solid var(--row-line); }
.bar, .track { position: relative; display: flex; height: 10px; border-radius: 5px; background: var(--line); overflow: hidden; }
.track { overflow: visible; }
.seg--0 { background: var(--ink); }
.seg--1 { background: var(--ink-soft); }
.seg--2 { background: var(--signal); }
.seg--3 { background: var(--panel); }
.fill { display: block; height: 10px; border-radius: 5px; background: var(--ink); }
.fill.warn { background: var(--signal); }
.mark { position: absolute; top: -3px; width: 2px; height: 16px; background: var(--signal); }
</style>
