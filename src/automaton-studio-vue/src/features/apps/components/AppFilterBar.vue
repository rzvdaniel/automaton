<script setup lang="ts">
/** The search box and filter chips shared by the Apps list and the Catalog. */
export interface FilterPill {
  value: string
  label: string
  icon?: string
}

defineProps<{
  pills: FilterPill[]
  active: (value: string) => boolean
  label: string
  groupLabel?: string
  /** Leave out to show the chips without a search box. */
  placeholder?: string
  /** Show the label as visible text before the chips, so a row of chips reads on purpose. */
  showLabel?: boolean
}>()
const search = defineModel<string>('search', { default: '' })
defineEmits<{ select: [value: string] }>()
</script>

<template>
  <div class="apps-toolbar">
    <label v-if="placeholder" class="apps-search">
      <span class="sr-only">{{ label }}</span>
      <i class="pi pi-search" aria-hidden="true" />
      <input v-model="search" type="search" :placeholder="placeholder" :aria-label="label" />
    </label>
    <span v-if="showLabel" class="apps-toolbar__label" aria-hidden="true">{{ label }}</span>
    <div class="apps-pills" role="group" :aria-label="groupLabel ?? label">
      <button
        v-for="pill in pills"
        :key="pill.value"
        type="button"
        class="apps-pill"
        :class="{ 'apps-pill--active': active(pill.value) }"
        :aria-pressed="active(pill.value)"
        @click="$emit('select', pill.value)"
      ><i v-if="pill.icon" :class="pill.icon" aria-hidden="true" /> {{ pill.label }}</button>
    </div>
  </div>
</template>

<style scoped>
.apps-toolbar { display: flex; flex-wrap: wrap; align-items: center; gap: 12px; margin-bottom: 16px; }
.apps-search { position: relative; flex: 1 1 240px; max-width: 360px; }
.apps-search i { position: absolute; left: 12px; top: 50%; transform: translateY(-50%); color: var(--ink-soft); }
.apps-search input {
  width: 100%; min-height: 44px; padding: 8px 12px 8px 34px; box-sizing: border-box;
  border: 1px solid var(--line); border-radius: 999px; background: var(--paper); color: var(--ink); font: inherit; font-size: 14px;
}
.apps-search input:focus-visible { outline: 2px solid var(--signal); outline-offset: 2px; }
.apps-toolbar__label { color: var(--ink-soft); font-size: 13px; font-weight: 600; }
.sr-only { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); white-space: nowrap; }
.apps-pills { display: flex; flex-wrap: wrap; gap: 8px; }
.apps-pill {
  min-height: 44px; padding: 6px 16px; border: 1px solid var(--line); border-radius: 999px;
  background: transparent; color: var(--ink); font: inherit; font-size: 14px; cursor: pointer;
}
.apps-pill--active { background: var(--ink); border-color: var(--ink); color: var(--paper); }
.apps-pill:focus-visible { outline: 2px solid var(--signal); outline-offset: 2px; }
</style>
