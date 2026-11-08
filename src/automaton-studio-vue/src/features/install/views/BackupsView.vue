<script setup lang="ts">
import BackupsPanel from '../components/BackupsPanel.vue'
</script>
<template>
  <div class="flex flex-column gap-3">
    <div>
      <h1 class="text-2xl font-bold mb-2">Backups</h1>
      <p class="m-0 text-color-secondary">
        The server backs up its database and the keys that decrypt it every night. Backups and restores run on the
        server; this page shows what exists and the command for each step.
      </p>
    </div>
    <BackupsPanel />
  </div>
</template>
