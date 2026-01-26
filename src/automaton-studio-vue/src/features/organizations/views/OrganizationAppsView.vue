<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import Button from 'primevue/button'
import Card from 'primevue/card'

const route = useRoute()
const router = useRouter()

const organizationId = computed(() => route.params.id as string)

function navigateBack() {
  router.push({ name: 'organization-dashboard', params: { id: organizationId.value } })
}
</script>

<template>
  <div class="organization-apps">
    <div class="page-header">
      <div class="flex align-items-center gap-3">
        <Button
          icon="pi pi-arrow-left"
          text
          rounded
          @click="navigateBack"
        />
        <div>
          <h1>App Assignments</h1>
          <p class="text-muted">Manage app access for organization members</p>
        </div>
      </div>
    </div>

    <Card>
      <template #content>
        <div class="placeholder-content">
          <i class="pi pi-th-large" style="font-size: 3rem; color: var(--p-text-muted-color)"></i>
          <h3>App Assignment Management</h3>
          <p class="text-muted">
            This feature allows OrgAdmins to assign apps to AppUser members.<br>
            Implementation coming soon.
          </p>
        </div>
      </template>
    </Card>
  </div>
</template>

<style scoped>
.organization-apps {
  padding: 2rem;
}

.page-header {
  display: flex;
  justify-content: space-between;
  align-items: start;
  margin-bottom: 2rem;
}

.page-header h1 {
  margin: 0 0 0.5rem 0;
  font-size: 2rem;
  font-weight: 600;
}

.text-muted {
  color: var(--p-text-muted-color);
}

.placeholder-content {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 4rem 2rem;
  text-align: center;
  gap: 1rem;
}
</style>
