<script setup lang="ts">
import PageHead from '@/shared/components/PageHead.vue'
import BackupsPanel from '../components/BackupsPanel.vue'
</script>
<template>
  <div class="studio-page" style="max-width: 1400px; margin: 0 auto">
    <PageHead title="Backups" line="Nightly copies of the database and the keys that decrypt it." />
    <BackupsPanel />
  </div>
</template>
