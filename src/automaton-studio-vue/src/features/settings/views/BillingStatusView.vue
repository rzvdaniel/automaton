<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { useRouter } from 'vue-router'
import billingService, { type BillingStatusResponse } from '../services/billingService'
import { useSubscriptionsEnabled } from '@/shared/composables/useSubscriptionsEnabled'

const router = useRouter()
const subscriptionsEnabled = useSubscriptionsEnabled()

const status = ref<BillingStatusResponse | null>(null)
const loading = ref(true)
const portalLoading = ref(false)
const error = ref<string | null>(null)

const usagePercent = computed(() => {
  if (!status.value || status.value.monthlyCostLimitUsd === 0) return 0
  return Math.min(100, Math.round((status.value.currentMonthCostUsd / status.value.monthlyCostLimitUsd) * 100))
})

const usageSeverity = computed(() => {
  if (usagePercent.value >= 100) return 'danger'
  if (usagePercent.value >= 80) return 'warn'
  return 'success'
})

onMounted(async () => {
  try {
    status.value = await billingService.getBillingStatus()
  } catch (e: any) {
    error.value = e?.response?.data?.message ?? 'Failed to load billing status'
  } finally {
    loading.value = false
  }
})

async function goToPortal() {
  portalLoading.value = true
  try {
    const { portalUrl } = await billingService.createPortalSession()
    window.location.href = portalUrl
  } catch (e: any) {
    error.value = e?.response?.data?.message ?? 'Failed to open billing portal'
    portalLoading.value = false
  }
}
</script>

<template>
  <div class="billing-view p-6 max-w-2xl mx-auto">
    <div class="flex items-center gap-3 mb-6">
      <i class="pi pi-credit-card text-2xl text-primary"></i>
      <h1 class="text-2xl font-semibold">Billing</h1>
    </div>

    <ProgressSpinner v-if="loading" class="block mx-auto mt-10" />

    <Message v-else-if="error" severity="error" :closable="false">{{ error }}</Message>

    <template v-else-if="status">
      <!-- Current Plan Card -->
      <Card class="mb-4">
        <template #title>
          <div class="flex items-center justify-between">
            <span>Current Plan</span>
            <Tag :value="status.planName" severity="info" />
          </div>
        </template>
        <template #content>
          <div class="flex items-center justify-between text-sm text-surface-600 mb-1">
            <span>Monthly price</span>
            <span class="font-medium text-surface-800 dark:text-surface-200">
              ${{ status.priceUsd.toFixed(2) }} / month
            </span>
          </div>
          <div class="flex items-center justify-between text-sm text-surface-600">
            <span>AI budget</span>
            <span class="font-medium text-surface-800 dark:text-surface-200">
              ${{ status.monthlyCostLimitUsd.toFixed(2) }} / month
            </span>
          </div>
        </template>
      </Card>

      <!-- Usage Card -->
      <Card class="mb-4">
        <template #title>This Month's AI Usage</template>
        <template #content>
          <div class="flex items-center justify-between text-sm mb-2">
            <span class="text-surface-600">
              ${{ status.currentMonthCostUsd.toFixed(2) }} of ${{ status.monthlyCostLimitUsd.toFixed(2) }} used
            </span>
            <span :class="usagePercent >= 80 ? 'font-bold text-orange-600' : 'text-surface-600'">
              {{ usagePercent }}%
            </span>
          </div>
          <ProgressBar :value="usagePercent" :showValue="false" :class="`h-3 progress-${usageSeverity}`" />
        </template>
      </Card>

      <!-- Actions -->
      <div class="flex gap-3">
        <Button
          v-if="status.stripeSubscriptionId && subscriptionsEnabled"
          label="Manage Subscription"
          icon="pi pi-external-link"
          :loading="portalLoading"
          @click="goToPortal"
        />
        <Button
          label="Change Plan"
          icon="pi pi-tag"
          severity="secondary"
          @click="router.push('/pricing')"
        />
      </div>
    </template>
  </div>
</template>

<style scoped>
:deep(.progress-success .p-progressbar-value) { background: var(--p-green-500); }
:deep(.progress-warn   .p-progressbar-value)  { background: var(--p-orange-500); }
:deep(.progress-danger .p-progressbar-value)  { background: var(--p-red-500); }
</style>
