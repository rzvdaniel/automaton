<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { useRouter } from 'vue-router'
import Button from 'primevue/button'
import Card from 'primevue/card'
import Dialog from 'primevue/dialog'
import { useSubscriptionsEnabled } from '@/shared/composables/useSubscriptionsEnabled'
import { usePaidPlansAvailable } from '@/shared/composables/usePaidPlansAvailable'
import billingService, { type BillingStatusResponse } from '../services/billingService'
import { adminSubscriptionPlansService } from '@/features/admin/services/adminSubscriptionPlansService'
import type { SubscriptionPlan } from '@/features/admin/types/admin.types'

const router = useRouter()
const subscriptionsEnabled = useSubscriptionsEnabled()
const { loading: plansLoading, hasPaidPlans, ensureLoaded } = usePaidPlansAvailable()
const loading = ref(true)
const checkoutLoadingId = ref<string | null>(null)
const currentStatus = ref<BillingStatusResponse | null>(null)
const plans = ref<SubscriptionPlan[]>([])

// Upgrade confirmation dialog
const confirmDialogVisible = ref(false)
const pendingPlan = ref<SubscriptionPlan | null>(null)

onMounted(async () => {
  if (!subscriptionsEnabled) { loading.value = false; return }
  try {
    await Promise.all([
      ensureLoaded(),
      adminSubscriptionPlansService.getPlans().then((result) => {
        plans.value = result
      }).catch(() => {
        plans.value = []
      }),
      billingService.getBillingStatus().then(s => { currentStatus.value = s }).catch(() => {}),
    ])
  } finally {
    loading.value = false
  }
})

const showPlanCatalog = computed(() => subscriptionsEnabled && hasPaidPlans.value)
const pageTitle = computed(() => showPlanCatalog.value ? 'Choose your runner plan' : 'Free for now')
const pageSubtitle = computed(() => showPlanCatalog.value
  ? 'Upgrade when you want Automaton to host a ready-to-use runner for your apps.'
  : 'Future paid plans will add hosted runners, so users can run app backends without configuring infrastructure.')

function planFeatures(plan: SubscriptionPlan): string[] {
  const runner = plan.capabilities.hostedRunner
  if (!runner.enabled) {
    return ['Self-hosted runners', 'Create apps and projects', 'Use your own infrastructure']
  }

  return [
    'Hosted runner included',
    'Managed resource specification',
    'Persistent backend execution',
  ]
}

// Highlight the middle plan (Pro / highest non-last paid plan)
const highlightedIndex = computed(() => {
  if (plans.value.length <= 2) return -1
  return Math.floor(plans.value.length / 2)
})

const isCurrentPlan = (plan: SubscriptionPlan) =>
  currentStatus.value?.planName === plan.name

function upgrade(plan: SubscriptionPlan) {
  if (plan.isDefault || !plan.priceUsd || isCurrentPlan(plan)) return
  // If user already has an active subscription, show confirmation with proration warning
  if (currentStatus.value?.hasActiveSubscription) {
    pendingPlan.value = plan
    confirmDialogVisible.value = true
    return
  }
  doCheckout(plan)
}

async function confirmUpgrade() {
  if (!pendingPlan.value) return
  confirmDialogVisible.value = false
  await doCheckout(pendingPlan.value)
  pendingPlan.value = null
}

async function doCheckout(plan: SubscriptionPlan) {
  checkoutLoadingId.value = plan.id
  try {
    const { checkoutUrl } = await billingService.createCheckoutSession(plan.id)
    window.location.href = checkoutUrl
  } catch {
    checkoutLoadingId.value = null
  }
}

const buttonLabel = (plan: SubscriptionPlan) => {
  if (isCurrentPlan(plan)) return 'Current plan'
  if (plan.isDefault && !currentStatus.value?.hasActiveSubscription) return 'Current plan'
  if (plan.priceUsd === 0) return 'Downgrade to Free'
  return `Upgrade to ${plan.name}`
}

const isDisabled = (plan: SubscriptionPlan) =>
  isCurrentPlan(plan) || (plan.isDefault && !currentStatus.value?.hasActiveSubscription)
</script>

<template>
  <div class="pricing-view">
    <div class="pricing-header">
      <Button
        icon="pi pi-arrow-left"
        text
        label="Back"
        class="mb-4"
        @click="router.back()"
      />
      <h1>{{ pageTitle }}</h1>
      <p class="text-color-secondary">{{ pageSubtitle }}</p>
    </div>

    <div v-if="loading || plansLoading" class="flex justify-center py-10">
      <i class="pi pi-spin pi-spinner" style="font-size: 2rem" />
    </div>

    <div v-else-if="!showPlanCatalog" class="coming-soon">
      <i class="pi pi-clock text-5xl text-color-secondary mb-4" />
      <h2>Paid subscriptions coming later</h2>
      <p class="text-color-secondary">
        Everyone is on the <strong>Free</strong> plan for now.
        The free plan is here to stay. Future paid plans will add hosted runners for users who want Automaton-managed backend execution.
      </p>
    </div>

    <div v-else class="plans-grid">
      <Card
        v-for="(plan, index) in plans"
        :key="plan.id"
        :class="['plan-card', { 'plan-card--highlighted': index === highlightedIndex }]"
      >
        <template #content>
          <div v-if="index === highlightedIndex" class="plan-badge">Most Popular</div>
          <div class="plan-name">{{ plan.name }}</div>
          <div class="plan-price">
            <span v-if="plan.priceUsd > 0">
              <span class="price-amount">${{ plan.priceUsd.toFixed(0) }}</span>
              <span class="price-period text-color-secondary">/month</span>
            </span>
            <span v-else class="price-amount">Free</span>
          </div>
          <p class="plan-description text-color-secondary text-sm">{{ plan.description }}</p>
          <ul class="plan-features">
            <li v-for="feature in planFeatures(plan)" :key="feature">
              <i class="pi pi-check text-green-500 mr-2" />{{ feature }}
            </li>
          </ul>
          <Button
            :label="buttonLabel(plan)"
            :disabled="isDisabled(plan)"
            :loading="checkoutLoadingId === plan.id"
            :outlined="index !== highlightedIndex"
            class="w-full mt-4"
            @click="upgrade(plan)"
          />
        </template>
      </Card>
    </div>

    <p v-if="showPlanCatalog" class="pricing-note text-color-secondary text-sm text-center mt-4">
      Payment powered by Stripe. Your subscription activates instantly after checkout.
    </p>
  </div>

  <!-- Upgrade confirmation dialog -->
  <Dialog
    v-model:visible="confirmDialogVisible"
    header="Confirm plan change"
    modal
    style="width: 420px"
  >
    <div v-if="pendingPlan" class="flex flex-column gap-3">
      <p>
        You're switching from <strong>{{ currentStatus?.planName }}</strong>
        to <strong>{{ pendingPlan.name }}</strong> (${{ pendingPlan.priceUsd.toFixed(2) }}/month).
      </p>
      <p class="text-sm text-color-secondary">
        Your card will be charged a <strong>prorated amount</strong> immediately for the
        remainder of this billing period. Your next full invoice will be on your regular billing date.
      </p>
      <p class="text-sm text-color-secondary">
        The new plan activates as soon as payment succeeds.
      </p>
    </div>
    <template #footer>
      <Button label="Cancel" text @click="confirmDialogVisible = false" />
      <Button
        :label="`Switch to ${pendingPlan?.name}`"
        icon="pi pi-check"
        :loading="!!checkoutLoadingId"
        @click="confirmUpgrade"
      />
    </template>
  </Dialog>
</template>

<style scoped>
.pricing-view {
  max-width: 960px;
  margin: 0 auto;
  padding: 2rem;
}

.pricing-header {
  text-align: center;
  margin-bottom: 2.5rem;
}

.pricing-header h1 {
  font-size: 2rem;
  font-weight: 700;
  margin-bottom: 0.5rem;
}

.plans-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(210px, 1fr));
  gap: 1.5rem;
}

.plan-card {
  position: relative;
}

.plan-card--highlighted {
  border: 2px solid var(--p-primary-color);
}

.plan-badge {
  position: absolute;
  top: -12px;
  left: 50%;
  transform: translateX(-50%);
  background: var(--p-primary-color);
  color: white;
  font-size: 0.75rem;
  font-weight: 600;
  padding: 2px 12px;
  border-radius: 12px;
}

.plan-name {
  font-size: 1.1rem;
  font-weight: 700;
  margin-bottom: 0.5rem;
}

.plan-price {
  display: flex;
  align-items: baseline;
  gap: 4px;
  margin-bottom: 0.5rem;
}

.price-amount {
  font-size: 2rem;
  font-weight: 700;
}

.plan-description {
  margin-bottom: 1rem;
  min-height: 2.5rem;
}

.plan-features {
  list-style: none;
  padding: 0;
  margin: 0;
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  font-size: 0.9rem;
}

.pricing-note {
  opacity: 0.7;
}

.coming-soon {
  text-align: center;
  padding: 3rem 1rem;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.75rem;
}
</style>
