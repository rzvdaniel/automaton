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
import { usePaidPlansAvailable } from '@/shared/composables/usePaidPlansAvailable'

const router = useRouter()
const subscriptionsEnabled = useSubscriptionsEnabled()
const { hasPaidPlans, ensureLoaded } = usePaidPlansAvailable()

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
    await Promise.all([
      billingService.getBillingStatus().then((result) => {
        status.value = result
      }),
      ensureLoaded(),
    ])
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

const plansActionLabel = computed(() => hasPaidPlans.value ? 'Change Plan' : 'Plans Soon')
const aiUpgradeLabel = computed(() => hasPaidPlans.value ? 'Upgrade' : 'Plans Soon')
const aiAccessHint = computed(() => hasPaidPlans.value
  ? 'Add your own API key or upgrade for managed AI'
  : 'Add your own API key for AI access. Paid subscriptions are coming later.')
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
      <div class="billing-cards mb-5">

        <!-- Current Plan Card -->
        <Card class="billing-card">
          <template #content>
            <div class="billing-card-label">Current Plan</div>
            <div class="billing-card-value">
              {{ status.planName }}
              <Tag v-if="status.hasActiveSubscription" value="Active" severity="success" class="ml-2" />
            </div>
            <div class="billing-card-meta">
              <span v-if="status.priceUsd > 0">${{ status.priceUsd.toFixed(2) }} / month</span>
              <span v-else>Free</span>
            </div>
          </template>
        </Card>

        <!-- AI Usage Card -->
        <Card class="billing-card">
          <template #content>
            <div class="billing-card-label">AI Usage</div>

            <!-- BYOK: user has own keys, no managed budget -->
            <template v-if="status.hasByokKeys && status.monthlyCostLimitUsd === 0">
              <div class="billing-card-value byok-value">
                <i class="pi pi-key" style="color: var(--p-green-500)"></i>
                External (BYOK)
              </div>
              <div class="billing-card-meta">Using your own API keys — no AI charges</div>
              <Button
                label="Manage AI Keys"
                icon="pi pi-key"
                size="small"
                text
                class="mt-2 pl-0"
                @click="router.push('/ai-keys')"
              />
            </template>

            <!-- Paid: managed AI budget -->
            <template v-else-if="status.monthlyCostLimitUsd > 0">
              <div class="billing-card-value">
                ${{ status.currentMonthCostUsd.toFixed(2) }}
                <span class="billing-card-of">/ ${{ status.monthlyCostLimitUsd.toFixed(2) }} included</span>
              </div>
              <ProgressBar
                :value="usagePercent"
                :show-value="false"
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

            <!-- Free, no keys, no budget -->
            <template v-else>
              <div class="billing-card-value" style="font-size: 1rem; font-weight: 500; color: var(--p-text-muted-color)">
                No AI access configured
              </div>
              <div class="billing-card-meta mb-3">{{ aiAccessHint }}</div>
              <div class="flex gap-2">
                <Button label="Add AI Key" icon="pi pi-key" size="small" outlined @click="router.push('/ai-keys')" />
                <Button :label="aiUpgradeLabel" icon="pi pi-tag" size="small" severity="secondary" outlined @click="router.push('/pricing')" />
              </div>
            </template>
          </template>
        </Card>

        <!-- Infrastructure Card -->
        <Card class="billing-card">
          <template #content>
            <div class="billing-card-label">Infrastructure</div>
            <div class="billing-card-value" style="font-size: 1.1rem">
              <i class="pi pi-server mr-2 text-color-secondary"></i>
              Self-hosted
            </div>
            <div class="billing-card-meta">Using your own runners — no infrastructure charges</div>
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
          :label="plansActionLabel"
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

.billing-cards {
  display: grid;
  grid-template-columns: 1fr 1fr 1fr;
  gap: 1rem;
}

@media (max-width: 900px) {
  .billing-cards {
    grid-template-columns: 1fr 1fr;
  }
}

@media (max-width: 600px) {
  .billing-cards {
    grid-template-columns: 1fr;
  }
}

.byok-value {
  gap: 0.5rem;
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
