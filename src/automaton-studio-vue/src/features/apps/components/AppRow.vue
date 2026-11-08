<script setup lang="ts">
import { RouterLink } from 'vue-router'
import AppMark from './AppMark.vue'
import type { AppState } from '../utils/app-list-display'

/** One app in the Apps list: a link to the app page, or a button when the person cannot open the page. */
defineProps<{
  id: string
  name: string
  line: string
  imageUrl?: string | null
  to?: string
  state?: AppState
  busy?: boolean
}>()
</script>

<template>
  <component :is="to ? RouterLink : 'button'" class="app-row" :to="to" :type="to ? undefined : 'button'" :aria-busy="busy || undefined">
    <AppMark :id="id" :name="name" :image-url="imageUrl" />
    <span class="app-row__text">
      <span class="app-row__name">{{ name }}</span>
      <span class="app-row__line">{{ line }}</span>
    </span>
    <span v-if="state" class="app-row__state" :class="`app-row__state--${state.tone}`">{{ state.label }}</span>
  </component>
</template>

<style scoped>
.app-row {
  display: flex;
  align-items: center;
  gap: 12px;
  width: 100%;
  min-height: 44px;
  padding: 12px 14px;
  border: 1px solid var(--row-line);
  border-radius: 12px;
  background: transparent;
  color: var(--ink);
  font: inherit;
  text-align: left;
  text-decoration: none;
  cursor: pointer;
}
.app-row:hover { border-color: var(--signal); }
.app-row:focus-visible { outline: 2px solid var(--signal); outline-offset: 2px; }
.app-row__text { flex: 1; min-width: 0; display: flex; flex-direction: column; gap: 2px; }
.app-row__name { font-weight: 600; font-size: 15px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.app-row__line { color: var(--ink-soft); font-size: 13px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }

.app-row__state {
  flex: none;
  padding: 3px 9px;
  border-radius: 999px;
  font-family: var(--mono);
  font-size: 11.5px;
  line-height: 1.3;
}
.app-row__state--live { background: var(--signal); color: var(--on-signal); font-weight: 600; }
.app-row__state--ok { border: 1px solid var(--ok); color: var(--ok); }
.app-row__state--failing { background: color-mix(in srgb, var(--signal) 18%, transparent); color: var(--signal-ink); }
.app-row__state--stopped { background: var(--row-line); color: var(--ink-soft); }
@media (max-width: 640px) {
  .app-row { flex-wrap: wrap; }
  .app-row__text { flex-basis: calc(100% - 60px); }
}
</style>
