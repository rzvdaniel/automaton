import { computed, readonly, ref } from 'vue'
import api from '@/shared/services/api/axios-instance'

interface SubscriptionPlanCatalogItem {
  id: string
  priceUsd: number
}

const plans = ref<SubscriptionPlanCatalogItem[]>([])
const loading = ref(false)
const loaded = ref(false)

async function loadPlans() {
  if (loading.value || loaded.value) {
    return
  }

  loading.value = true
  try {
    const response = await api.get<SubscriptionPlanCatalogItem[]>('/api/account/getsubscriptionplans')
    plans.value = response.data
    loaded.value = true
  } catch {
    plans.value = []
    loaded.value = true
  } finally {
    loading.value = false
  }
}

const hasPaidPlans = computed(() => plans.value.some((plan) => plan.priceUsd > 0))

export function usePaidPlansAvailable() {
  void loadPlans()

  return {
    plans: readonly(plans),
    loading: readonly(loading),
    loaded: readonly(loaded),
    hasPaidPlans,
    ensureLoaded: loadPlans,
  }
}