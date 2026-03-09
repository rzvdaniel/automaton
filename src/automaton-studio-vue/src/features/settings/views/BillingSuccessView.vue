<script setup lang="ts">
import { useRouter } from 'vue-router'
const router = useRouter()
</script>

<template>
  <div class="flex flex-col items-center justify-center min-h-[60vh] text-center p-6">
    <div class="w-16 h-16 rounded-full bg-green-100 flex items-center justify-center mb-4">
      <i class="pi pi-check text-green-600 text-3xl"></i>
    </div>
    <h1 class="text-2xl font-semibold mb-2">You're all set!</h1>
    <p class="text-surface-600 mb-6 max-w-sm">
      Your subscription has been activated. Your new AI budget is now available.
    </p>
    <div class="flex gap-3">
      <Button label="Go to Dashboard" icon="pi pi-home" @click="router.push('/')" />
      <Button label="View Billing" icon="pi pi-credit-card" severity="secondary" @click="router.push('/billing')" />
    </div>
  </div>
</template>
