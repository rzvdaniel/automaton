<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { useRouter } from 'vue-router'
import Card from 'primevue/card'
import Button from 'primevue/button'
import Tag from 'primevue/tag'
import ProgressBar from 'primevue/progressbar'
import ProgressSpinner from 'primevue/progressspinner'
import Message from 'primevue/message'
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
  <div class="billing-view">
    <div class="flex items-center gap-3 mb-6">
      <i class="pi pi-credit-card text-2xl text-primary"></i>
      <h1 class="text-2xl font-semibold">Billing</h1>
    </div>

    <ProgressSpinner v-if="loading" class="block mx-auto mt-10" />

    <Message v-else-if="error" severity="error" :closable="false">{{ error }}</Message>

    <template v-else-if="status">
      <!-- Cards row -->
      <div class="billing-grid mb-5">

        <!-- Current Plan Card -->
        <Card class="billing-card">
          <template #content>
            <div class="billing-card-label">Current Plan</div>
            <div class="billing-card-value">
              {{ status.planName }}
              <Tag v-if="status.hasActiveSubscription" value="Active" severity="success" class="ml-2" />
            </div>
            <div class="billing-card-meta">
              <span>${{ status.priceUsd.toFixed(2) }} / month</span>
              <span class="billing-card-divider">·</span>
              <span>${{ status.monthlyCostLimitUsd.toFixed(2) }} AI budget</span>
            </div>
          </template>
        </Card>

        <!-- Usage Card -->
        <Card class="billing-card">
          <template #content>
            <div class="billing-card-label">AI Usage This Month</div>
            <div class="billing-card-value">
              ${{ status.currentMonthCostUsd.toFixed(2) }}
              <span class="billing-card-of">/ ${{ status.monthlyCostLimitUsd.toFixed(2) }}</span>
            </div>
            <ProgressBar
              :value="usagePercent"
              :showValue="false"
              class="billing-progress mb-2"
              :class="`progress-${usageSeverity}`"
            />
            <div class="billing-card-meta">
              <span
                :class="{
                  'text-orange-500 font-semibold': usagePercent >= 80,
                  'text-red-500 font-semibold': usagePercent >= 100,
                }"
              >{{ usagePercent }}% used</span>
              <span class="billing-card-divider">·</span>
              <span>${{ (status.monthlyCostLimitUsd - status.currentMonthCostUsd).toFixed(2) }} remaining</span>
            </div>
          </template>
        </Card>

      </div>

      <!-- Actions -->
      <div class="flex gap-3">
        <Button
          v-if="status.hasActiveSubscription && subscriptionsEnabled"
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
.billing-view {
  padding: 1.5rem;
  max-width: 760px;
}

.billing-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 1rem;
}

@media (max-width: 600px) {
  .billing-grid {
    grid-template-columns: 1fr;
  }
}

.billing-card {
  height: 100%;
}

.billing-card-label {
  font-size: 0.75rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: var(--p-text-muted-color);
  margin-bottom: 0.35rem;
}

.billing-card-value {
  font-size: 1.5rem;
  font-weight: 700;
  margin-bottom: 0.4rem;
  display: flex;
  align-items: center;
}

.billing-card-of {
  font-size: 1rem;
  font-weight: 400;
  color: var(--p-text-muted-color);
  margin-left: 0.25rem;
}

.billing-card-meta {
  font-size: 0.8rem;
  color: var(--p-text-muted-color);
  display: flex;
  align-items: center;
  gap: 0.4rem;
}

.billing-card-divider {
  opacity: 0.4;
}

.billing-progress {
  height: 6px !important;
}

:deep(.billing-progress.p-progressbar) {
  height: 6px;
}

:deep(.progress-success .p-progressbar-value) { background: var(--p-green-500); }
:deep(.progress-warn   .p-progressbar-value)  { background: var(--p-orange-500); }
:deep(.progress-danger .p-progressbar-value)  { background: var(--p-red-500); }
</style>
