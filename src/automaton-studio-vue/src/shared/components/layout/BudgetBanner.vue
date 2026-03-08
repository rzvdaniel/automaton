<script setup lang="ts">
import { useRouter } from 'vue-router'
import Button from 'primevue/button'
import { useBudgetBanner } from '@/shared/composables/useBudgetBanner'
import { useSubscriptionsEnabled } from '@/shared/composables/useSubscriptionsEnabled'

const { percentUsed, currentMonthCostUsd, monthlyCostLimitUsd, showWarning, showDanger } = useBudgetBanner()
const subscriptionsEnabled = useSubscriptionsEnabled()
const router = useRouter()
</script>

<template>
  <div
    v-if="showDanger"
    class="budget-banner budget-banner--danger"
  >
    <i class="pi pi-exclamation-circle mr-2" />
    <span>
      You've reached your monthly AI budget of
      <strong>${{ monthlyCostLimitUsd.toFixed(2) }}</strong>.
      AI responses are now using a lower-cost model.
    </span>
    <Button
      v-if="subscriptionsEnabled"
      label="Upgrade"
      size="small"
      class="ml-3"
      @click="router.push('/pricing')"
    />
  </div>

  <div
    v-else-if="showWarning"
    class="budget-banner budget-banner--warning"
  >
    <i class="pi pi-exclamation-triangle mr-2" />
    <span>
      You've used <strong>{{ percentUsed }}%</strong> of your monthly AI budget
      (${{ currentMonthCostUsd.toFixed(2) }} / ${{ monthlyCostLimitUsd.toFixed(2) }}).
    </span>
    <Button
      v-if="subscriptionsEnabled"
      label="Upgrade"
      size="small"
      severity="warn"
      class="ml-3"
      @click="router.push('/pricing')"
    />
  </div>
</template>

<style scoped>
.budget-banner {
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 0.4rem 1rem;
  font-size: 0.875rem;
  width: 100%;
}

.budget-banner--warning {
  background-color: var(--p-yellow-100);
  color: var(--p-yellow-900);
}

.app-dark .budget-banner--warning {
  background-color: var(--p-yellow-900);
  color: var(--p-yellow-100);
}

.budget-banner--danger {
  background-color: var(--p-red-100);
  color: var(--p-red-900);
}

.app-dark .budget-banner--danger {
  background-color: var(--p-red-900);
  color: var(--p-red-100);
}
</style>
