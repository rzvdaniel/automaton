<script setup lang="ts">
import { computed } from 'vue'
import { RouterLink } from 'vue-router'
import AppMark from './AppMark.vue'
import type { AppState } from '../utils/app-list-display'

/** One app in the Apps list: a link to the app page, or a button when the person cannot open the page. */
const props = defineProps<{
  id: string
  name: string
  line: string
  imageUrl?: string | null
  to?: string
  /** The app itself: the row opens it in a new tab instead of linking to `to`. */
  href?: string
  state?: AppState
  busy?: boolean
  label?: string
}>()
defineEmits<{ open: [] }>()
const row = computed(() => props.href
  ? { is: 'a', attrs: { href: props.href, target: '_blank', rel: 'noopener' } }
  : props.to ? { is: RouterLink, attrs: { to: props.to } } : { is: 'button', attrs: { type: 'button' } })
</script>

<template>
  <div v-if="$slots.action" class="app-row app-row--split">
    <component :is="row.is" v-bind="row.attrs" class="app-row__main" :aria-label="label" @click="$emit('open')">
      <AppMark :id="id" :name="name" :image-url="imageUrl" />
      <span class="app-row__text">
        <span class="app-row__name">{{ name }}</span>
        <span class="app-row__line">{{ line }}</span>
        <slot name="meta" />
      </span>
      <slot name="tag" />
      <span v-if="state" class="app-row__state" :class="`app-row__state--${state.tone}`">{{ state.label }}</span>
    </component>
    <slot name="action" />
  </div>
  <component :is="row.is" v-else v-bind="row.attrs" class="app-row" :aria-busy="busy || undefined">
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
.app-row--split { padding: 0 14px 0 0; }
.app-row--split:focus-within { border-color: var(--signal); }
.app-row__main {
  flex: 1; min-width: 0; display: flex; align-items: center; gap: 12px; padding: 12px 0 12px 14px;
  border: 0; background: transparent; color: inherit; font: inherit; text-align: left; text-decoration: none; cursor: pointer;
}
.app-row__main:focus-visible { outline: 2px solid var(--signal); outline-offset: 2px; border-radius: 12px; }
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
  /* The action (Open, Update, Install) sits on its own line, so a wide one never squeezes the name. */
  .app-row--split .app-row__main { flex: 1 1 100%; }
}
</style>
