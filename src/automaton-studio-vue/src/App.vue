<script setup lang="ts">
// App entry point - layout is handled by router
</script>

<template>
  <router-view />
</template>

<style>
/* Global styles */
html {
  overflow-y: auto;
}

/* Roboto font family per design spec */
body {
  font-family: 'Roboto', system-ui, -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif;
}

/* JetBrains Mono for code */
.font-mono,
code,
pre {
  font-family: 'JetBrains Mono', 'Fira Code', 'Consolas', monospace;
}
</style>
