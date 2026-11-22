<script setup lang="ts">
/** The title, one-line subtitle and small header buttons every Studio page starts with. */
defineProps<{ title: string; line?: string }>()
</script>

<template>
  <header class="page-head">
    <div>
      <h1 class="page-head__title">{{ title }}</h1>
      <p v-if="line" class="page-head__line">{{ line }}</p>
    </div>
    <div v-if="$slots.default" class="page-head__buttons"><slot /></div>
  </header>
</template>
