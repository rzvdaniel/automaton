<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { useRouter } from 'vue-router'
import Button from 'primevue/button'
import Card from 'primevue/card'
import api from '@/shared/services/api/axios-instance'
import { useSubscriptionsEnabled } from '@/shared/composables/useSubscriptionsEnabled'

interface SubscriptionPlan {
  id: string
  name: string
  description: string | null
  monthlyCostLimitUsd: number
  priceUsd: number
  canCreateProjects: boolean
  canCreateOrganizations: boolean
  isDefault: boolean
  sortOrder: number
}

const router = useRouter()
const subscriptionsEnabled = useSubscriptionsEnabled()
const plans = ref<SubscriptionPlan[]>([])
const loading = ref(true)

onMounted(async () => {
  if (!subscriptionsEnabled) { loading.value = false; return }
  try {
    const { data } = await api.get<SubscriptionPlan[]>('/api/account/getsubscriptionplans')
    plans.value = data
  } finally {
    loading.value = false
  }
})

function planFeatures(plan: SubscriptionPlan): string[] {
  const features: string[] = [
    `$${plan.monthlyCostLimitUsd.toFixed(0)} AI budget / month`,
  ]
  if (plan.canCreateProjects) features.push('Projects')
  if (plan.canCreateOrganizations) features.push('Organizations')
  return features
}

// Highlight the middle plan (Pro / highest non-last paid plan)
const highlightedIndex = computed(() => {
  if (plans.value.length <= 2) return -1
  return Math.floor(plans.value.length / 2)
})
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
      <h1>Choose your plan</h1>
      <p class="text-color-secondary">Upgrade to unlock more AI budget and collaboration features.</p>
    </div>

    <div v-if="loading" class="flex justify-center py-10">
      <i class="pi pi-spin pi-spinner" style="font-size: 2rem" />
    </div>

    <div v-else-if="!subscriptionsEnabled" class="coming-soon">
      <i class="pi pi-clock text-5xl text-color-secondary mb-4" />
      <h2>Paid plans coming soon</h2>
      <p class="text-color-secondary">
        Everyone is on the <strong>Free</strong> plan for now.
        Paid plans will be available shortly — stay tuned!
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
            :label="plan.isDefault ? 'Current plan' : `Upgrade to ${plan.name}`"
            :disabled="plan.isDefault"
            :outlined="index !== highlightedIndex"
            class="w-full mt-4"
          />
        </template>
      </Card>
    </div>

    <p class="pricing-note text-color-secondary text-sm text-center mt-4">
      Payment processing coming soon. Contact us to upgrade manually.
    </p>
  </div>
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

