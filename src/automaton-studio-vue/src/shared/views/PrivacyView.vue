<script setup lang="ts">
import LegalDocumentView from './LegalDocumentView.vue'
import markdown from '@/content/legal/privacy-policy.md?raw'
</script>

<template>
  <LegalDocumentView
    title="Privacy Policy"
    eyebrow="Preview"
    :markdown="markdown"
  />
</template>
