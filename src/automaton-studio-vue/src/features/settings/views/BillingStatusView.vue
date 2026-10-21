<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import Card from 'primevue/card'
import Button from 'primevue/button'
import Tag from 'primevue/tag'
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


onMounted(async () => {
  try {
    await Promise.all([
      billingService.getBillingStatus().then((result) => {
        status.value = result
      }),
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

        <!-- Infrastructure Card -->
        <Card class="billing-card">
          <template #content>
            <div class="billing-card-label">AI Keys</div>
            <div class="billing-card-value" style="font-size: 1.1rem">
              <i class="pi pi-key mr-2 text-color-secondary"></i>
              Bring your own
            </div>
            <div class="billing-card-meta mb-3">AI provider keys are managed separately from runner subscriptions.</div>
            <Button label="Manage AI Keys" icon="pi pi-key" size="small" text class="mt-2 pl-0" @click="router.push('/ai-keys')" />
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
          label="View plans"
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
  grid-template-columns: repeat(2, minmax(0, 1fr));
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

.billing-card-meta {
  font-size: 0.8rem;
  color: var(--p-text-muted-color);
  display: flex;
  align-items: center;
  gap: 0.4rem;
}

</style>
