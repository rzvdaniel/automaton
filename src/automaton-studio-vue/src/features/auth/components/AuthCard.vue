<script setup lang="ts">
import '../auth.css'

// Centred card for the sign-in related pages: mark and name above, heading, optional lead line.
defineProps<{ title: string; lead?: string }>()
</script>

<template>
  <div class="auth-page">
    <main class="auth-card">
      <div class="auth-brand"><span class="auth-mark" aria-hidden="true">&gt;_</span>Automaton</div>
      <h1 class="auth-heading">{{ title }}</h1>
      <p v-if="lead" class="auth-lead">{{ lead }}</p>
      <slot />
    </main>
  </div>
</template>
