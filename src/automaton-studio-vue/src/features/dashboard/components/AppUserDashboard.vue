<script setup lang="ts">
import { useRouter } from 'vue-router'
import Card from 'primevue/card'
import Button from 'primevue/button'

const router = useRouter()

const handleViewMyApps = () => {
  router.push('/my-apps')
}
</script>

<template>
  <div class="app-user-dashboard">
    <!-- Page Header -->
    <div class="flex align-items-center justify-content-between mb-4">
      <div>
        <h1 class="text-3xl font-bold m-0">Dashboard</h1>
        <p class="text-color-secondary mt-1 mb-0">Access your assigned applications</p>
      </div>
    </div>

    <!-- My Apps Card -->
    <Card>
      <template #title>
        <div class="flex align-items-center gap-2">
          <i class="pi pi-globe text-xl"></i>
          <span>My Apps</span>
        </div>
      </template>
      <template #content>
        <div class="flex flex-column align-items-center justify-content-center py-5">
          <i class="pi pi-globe text-color-secondary" style="font-size: 3rem"></i>
          <p class="text-color-secondary mt-3 mb-2">View your assigned applications</p>
          <p class="text-color-secondary text-sm m-0 text-center">
            Applications assigned to you by your organization will appear here
          </p>
          <Button
            label="View My Apps"
            icon="pi pi-arrow-right"
            class="mt-4"
            @click="handleViewMyApps"
          />
        </div>
      </template>
    </Card>
  </div>
</template>

<style scoped>
.app-user-dashboard {
  max-width: 1400px;
  margin: 0 auto;
}
</style>
