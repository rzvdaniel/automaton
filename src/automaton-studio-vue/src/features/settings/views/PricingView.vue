<script setup lang="ts">
import { useRouter } from 'vue-router'
import Button from 'primevue/button'
import Card from 'primevue/card'

const router = useRouter()

const plans = [
  {
    name: 'Free',
    price: '$0',
    period: '/month',
    description: 'Get started building apps',
    highlight: false,
    features: [
      '$2 AI budget / month',
      'Unlimited apps',
      'Unlimited runners (self-hosted)',
      'Community support',
    ],
    cta: 'Current plan',
    ctaDisabled: true,
  },
  {
    name: 'Starter',
    price: '$9',
    period: '/month',
    description: 'For solo builders who need more AI',
    highlight: false,
    features: [
      '$10 AI budget / month',
      'Everything in Free',
      'Projects',
    ],
    cta: 'Upgrade to Starter',
    ctaDisabled: false,
  },
  {
    name: 'Pro',
    price: '$29',
    period: '/month',
    description: 'For teams collaborating on apps',
    highlight: true,
    features: [
      '$30 AI budget / month',
      'Everything in Starter',
      'Organizations',
      'Priority support',
    ],
    cta: 'Upgrade to Pro',
    ctaDisabled: false,
  },
  {
    name: 'Business',
    price: '$99',
    period: '/month',
    description: 'For growing businesses',
    highlight: false,
    features: [
      '$100 AI budget / month',
      'Everything in Pro',
      'Dedicated support',
    ],
    cta: 'Upgrade to Business',
    ctaDisabled: false,
  },
]
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

    <div class="plans-grid">
      <Card
        v-for="plan in plans"
        :key="plan.name"
        :class="['plan-card', { 'plan-card--highlighted': plan.highlight }]"
      >
        <template #content>
          <div class="plan-badge" v-if="plan.highlight">Most Popular</div>
          <div class="plan-name">{{ plan.name }}</div>
          <div class="plan-price">
            <span class="price-amount">{{ plan.price }}</span>
            <span class="price-period text-color-secondary">{{ plan.period }}</span>
          </div>
          <p class="plan-description text-color-secondary text-sm">{{ plan.description }}</p>
          <ul class="plan-features">
            <li v-for="feature in plan.features" :key="feature">
              <i class="pi pi-check text-green-500 mr-2" />{{ feature }}
            </li>
          </ul>
          <Button
            :label="plan.cta"
            :disabled="plan.ctaDisabled"
            :outlined="!plan.highlight"
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
</style>
