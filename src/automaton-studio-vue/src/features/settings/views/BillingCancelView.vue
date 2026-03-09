<script setup lang="ts">
import { useRouter } from 'vue-router'
const router = useRouter()
</script>

<template>
  <div class="flex flex-col items-center justify-center min-h-[60vh] text-center p-6">
    <div class="w-16 h-16 rounded-full bg-surface-100 flex items-center justify-center mb-4">
      <i class="pi pi-times text-surface-500 text-3xl"></i>
    </div>
    <h1 class="text-2xl font-semibold mb-2">Checkout cancelled</h1>
    <p class="text-surface-600 mb-6 max-w-sm">
      No changes were made to your subscription. You can choose a plan whenever you're ready.
    </p>
    <Button label="View Plans" icon="pi pi-tag" @click="router.push('/pricing')" />
  </div>
</template>
