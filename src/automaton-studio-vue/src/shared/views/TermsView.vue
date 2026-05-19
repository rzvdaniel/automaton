<script setup lang="ts">
import LegalDocumentView from './LegalDocumentView.vue'
import markdown from '@/content/legal/terms-of-service.md?raw'
</script>

<template>
  <LegalDocumentView
    title="Terms of Service"
    eyebrow="Preview"
    :markdown="markdown"
  />
</template>
